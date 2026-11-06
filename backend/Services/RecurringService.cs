using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExpenseTracker.Api.Services;

// Recurring items create planned occurrences only. Paying an occurrence creates exactly one linked transaction
// (or receipt for income); the unique indexes make repeated generation and repeated payment harmless.
public class RecurringService
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public RecurringService(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public static bool Covers(RecurringItem item, YearMonth month) =>
        new YearMonth(item.StartYear, item.StartMonth) <= month
        && (item.EndYear is not int ey || month <= new YearMonth(ey, item.EndMonth ?? 12));

    public static DateOnly DueDate(YearMonth month, int dayOfMonth) =>
        new(month.Year, month.Month, Math.Clamp(dayOfMonth, 1, month.DaysInMonth));

    public async Task EnsureOccurrencesAsync(int userId, YearMonth from, YearMonth to)
    {
        var items = await _db.RecurringItems
            .Where(r => r.UserId == userId && r.IsActive)
            .ToListAsync();
        if (items.Count == 0) return;

        var seriesIds = items.Select(i => i.SeriesId).Distinct().ToList();
        var existing = (await _db.RecurringOccurrences
                .Where(o => seriesIds.Contains(o.SeriesId))
                .Select(o => new { o.SeriesId, o.Year, o.Month })
                .ToListAsync())
            .Select(o => (o.SeriesId, new YearMonth(o.Year, o.Month)))
            .ToHashSet();

        var added = false;
        for (var m = from; m <= to; m = m.AddMonths(1))
        {
            foreach (var item in items.Where(i => Covers(i, m)))
            {
                if (!existing.Add((item.SeriesId, m))) continue;
                _db.RecurringOccurrences.Add(new RecurringOccurrence
                {
                    UserId = userId,
                    RecurringItemId = item.Id,
                    SeriesId = item.SeriesId,
                    Year = m.Year,
                    Month = m.Month,
                    DueDate = DueDate(m, item.DayOfMonth),
                    Amount = item.Amount,
                    Status = OccurrenceStatus.Planned
                });
                added = true;
            }
        }

        if (!added) return;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request generated the same months first; their rows are equivalent.
            foreach (var entry in _db.ChangeTracker.Entries<RecurringOccurrence>().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
        }
    }

    public async Task<int> CountPlannedAsync(int userId, YearMonth month) =>
        await _db.RecurringOccurrences.CountAsync(o => o.UserId == userId && o.Year == month.Year && o.Month == month.Month && o.Status == OccurrenceStatus.Planned);

    public record PayResult(string? Error, RecurringOccurrence? Occurrence, bool AlreadyPaid);

    public async Task<PayResult> PayAsync(int userId, int occurrenceId, DateOnly? date, decimal? amount, Guid? clientRequestId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var occurrence = await _db.RecurringOccurrences
            .Include(o => o.RecurringItem)
            .FirstOrDefaultAsync(o => o.Id == occurrenceId && o.UserId == userId);
        if (occurrence == null) return new PayResult("Not found", null, false);
        if (occurrence.Status == OccurrenceStatus.Paid) return new PayResult(null, occurrence, true);

        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        var today = LocalCalendar.Today(_clock, user.TimeZone);
        var paidOn = date ?? (occurrence.DueDate <= today ? occurrence.DueDate : today);
        if (InputRules.Date(paidOn, "Payment date") is { } dateError) return new PayResult(dateError, null, false);
        if (paidOn > today) return new PayResult("A payment can't be dated in the future; leave it planned until it's paid.", null, false);

        var value = amount ?? occurrence.Amount;
        if (InputRules.Amount(value) is { } amountError) return new PayResult(amountError, null, false);

        var item = occurrence.RecurringItem;
        try
        {
            if (item.Kind == "Income")
            {
                var receipt = new IncomeReceipt
                {
                    UserId = userId,
                    Kind = ReceiptKind.Other,
                    CategoryId = item.CategoryId,
                    Status = ReceiptStatus.Received,
                    ReceivedDate = paidOn,
                    Amount = value,
                    Description = item.Description,
                    PaymentMethod = item.PaymentMethod,
                    RecurringOccurrenceId = occurrence.Id,
                    ClientRequestId = clientRequestId,
                    CreatedAt = DateTime.UtcNow,
                    Allocations = { new IncomeAllocation { Year = occurrence.Year, Month = occurrence.Month, Amount = value } }
                };
                _db.IncomeReceipts.Add(receipt);
                await _db.SaveChangesAsync();
                occurrence.IncomeReceiptId = receipt.Id;
            }
            else
            {
                var account = item.AccountId is int accountId
                    ? await _db.Accounts.FirstAsync(a => a.Id == accountId && a.UserId == userId)
                    : await DefaultData.GetOrCreateDefaultAccountAsync(_db, user);
                var transaction = new Transaction
                {
                    UserId = userId,
                    AccountId = account.Id,
                    CategoryId = item.CategoryId,
                    Amount = value,
                    Description = item.Description,
                    TransactionDate = paidOn.ToDateTime(TimeOnly.MinValue),
                    Kind = TxKind.Expense,
                    Status = TxStatus.Paid,
                    PaymentMethod = item.PaymentMethod,
                    RecurringOccurrenceId = occurrence.Id,
                    ClientRequestId = clientRequestId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.Transactions.Add(transaction);
                await _db.SaveChangesAsync();
                occurrence.TransactionId = transaction.Id;
            }

            occurrence.Status = OccurrenceStatus.Paid;
            occurrence.Amount = value;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException
            || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A second tap or another device paid it first; that payment stands and nothing is duplicated.
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            var current = await _db.RecurringOccurrences.AsNoTracking().FirstAsync(o => o.Id == occurrenceId);
            return new PayResult(null, current, true);
        }
        return new PayResult(null, occurrence, false);
    }

    // Unpaying removes the linked transaction/receipt and makes the occurrence planned again.
    public async Task<string?> UnpayAsync(int userId, int occurrenceId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var occurrence = await _db.RecurringOccurrences.FirstOrDefaultAsync(o => o.Id == occurrenceId && o.UserId == userId);
        if (occurrence == null) return "Not found";
        if (occurrence.Status != OccurrenceStatus.Paid) return null;

        if (occurrence.TransactionId is int tid)
        {
            var t = await _db.Transactions.FirstOrDefaultAsync(x => x.Id == tid && x.UserId == userId);
            if (t != null)
            {
                if (await _db.Transactions.AnyAsync(r => r.RefundOfId == t.Id))
                    return "This payment has refunds; remove them first.";
                _db.Transactions.Remove(t);
            }
        }
        if (occurrence.IncomeReceiptId is int rid)
        {
            var r = await _db.IncomeReceipts.FirstOrDefaultAsync(x => x.Id == rid && x.UserId == userId);
            if (r != null) _db.IncomeReceipts.Remove(r);
        }

        occurrence.Status = OccurrenceStatus.Planned;
        occurrence.TransactionId = null;
        occurrence.IncomeReceiptId = null;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return null;
    }

    // "This and future": end the current version before `from` and start a new version in the same series.
    // Paid and individually edited occurrences keep their values.
    public async Task<RecurringItem> ChangeFromAsync(RecurringItem item, YearMonth from, Action<RecurringItem> apply)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        RecurringItem target;
        if (new YearMonth(item.StartYear, item.StartMonth) >= from)
        {
            apply(item);
            target = item;
        }
        else
        {
            var before = from.AddMonths(-1);
            target = new RecurringItem
            {
                UserId = item.UserId,
                SeriesId = item.SeriesId,
                Kind = item.Kind,
                Description = item.Description,
                AccountId = item.AccountId,
                CategoryId = item.CategoryId,
                Amount = item.Amount,
                DayOfMonth = item.DayOfMonth,
                PaymentMethod = item.PaymentMethod,
                StartYear = from.Year,
                StartMonth = from.Month,
                EndYear = item.EndYear,
                EndMonth = item.EndMonth,
                IsActive = item.IsActive,
                CreatedAt = DateTime.UtcNow
            };
            apply(target);
            item.EndYear = before.Year;
            item.EndMonth = before.Month;
            _db.RecurringItems.Add(target);
        }
        await _db.SaveChangesAsync();

        var future = await _db.RecurringOccurrences
            .Where(o => o.SeriesId == item.SeriesId && o.Status == OccurrenceStatus.Planned && !o.IsOverridden
                && o.Year * 12 + o.Month - 1 >= from.Index)
            .ToListAsync();
        foreach (var o in future)
        {
            var month = new YearMonth(o.Year, o.Month);
            if (!target.IsActive || !Covers(target, month))
            {
                _db.RecurringOccurrences.Remove(o);
                continue;
            }
            o.RecurringItemId = target.Id;
            o.Amount = target.Amount;
            o.DueDate = DueDate(month, target.DayOfMonth);
        }
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return target;
    }
}
