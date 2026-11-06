using System.Text.Json;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExpenseTracker.Api.Services;

public record ServiceResult<T>(T? Value, string? Error = null, int Status = 200, object? Detail = null)
{
    public static ServiceResult<T> Ok(T value) => new(value);
    public static ServiceResult<T> Fail(string error, int status = 400, object? detail = null) => new(default, error, status, detail);
}

// The expense ledger. The daily matrix, the detailed ledger and the Additional view all read these rows.
public class ExpenseService
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly LedgerLoader _loader;
    private readonly ColumnService _columns;

    public ExpenseService(AppDbContext db, IClock clock, LedgerLoader loader, ColumnService columns)
    {
        _db = db;
        _clock = clock;
        _loader = loader;
        _columns = columns;
    }

    // ---------- Reads ----------

    private IQueryable<Transaction> Owned(int userId) =>
        _db.Transactions.Where(t => t.UserId == userId && t.Kind != TxKind.LegacyIncome);

    public async Task<ExpenseListDto> ListAsync(int userId, YearMonth month, ExpenseFilterDto filter)
    {
        var user = await _loader.UserAsync(userId);
        var start = month.FirstDay;
        var end = month.NextFirstDay;

        var monthRows = await Owned(userId)
            .Where(t => t.TransactionDate >= start && t.TransactionDate < end)
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.TransferAccount)
            .AsNoTracking()
            .ToListAsync();

        var refundIds = monthRows.Select(t => t.Id).ToList();
        var refunded = await Owned(userId)
            .Where(t => t.Kind == TxKind.Refund && t.RefundOfId != null && refundIds.Contains(t.RefundOfId.Value))
            .GroupBy(t => t.RefundOfId!.Value)
            .Select(g => new { Id = g.Key, Total = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(x => x.Id, x => x.Total);

        var columns = await _columns.ColumnsAsync(userId);
        var dtos = monthRows.OrderBy(t => t.TransactionDate).ThenBy(t => t.Id).Select(t => ToDto(t, refunded)).ToList();

        // Formula ranges cover the whole month so filtering never changes a calculated value.
        _columns.Compute(columns, dtos);

        var filtered = Apply(dtos, filter);
        var data = await _loader.LoadAsync(userId, month, month);
        var figures = LedgerCalculator.Single(data, month);

        return new ExpenseListDto
        {
            Month = month,
            Currency = user.Currency,
            Columns = columns.Select(c => c.Dto).ToList(),
            Layout = await _columns.LayoutAsync(userId, "expenses", month),
            Rows = filtered,
            Filtered = Totals(filtered),
            MonthTotals = new LedgerTotalsDto
            {
                Count = dtos.Count,
                Gross = figures.GrossSpending,
                Refunds = figures.Refunds,
                Net = figures.NetSpending,
                Planned = figures.PlannedSpending,
                Transfers = dtos.Where(d => d.Kind == TxKind.Transfer).Sum(d => d.Amount),
                Additional = figures.AdditionalNet
            },
            IsFiltered = filtered.Count != dtos.Count || HasFilter(filter)
        };
    }

    private static bool HasFilter(ExpenseFilterDto f) =>
        !string.IsNullOrWhiteSpace(f.Search) || f.CategoryId.HasValue || f.From.HasValue || f.To.HasValue
        || !string.IsNullOrEmpty(f.Status) || !string.IsNullOrEmpty(f.Kind) || f.Additional.HasValue || !string.IsNullOrEmpty(f.PaymentMethod);

    private static List<ExpenseRowDto> Apply(List<ExpenseRowDto> rows, ExpenseFilterDto f)
    {
        IEnumerable<ExpenseRowDto> q = rows;
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(r => r.Description.Contains(s, StringComparison.OrdinalIgnoreCase)
                || r.CategoryName.Contains(s, StringComparison.OrdinalIgnoreCase)
                || r.PaymentMethod.Contains(s, StringComparison.OrdinalIgnoreCase)
                || r.Custom.Values.Any(v => v.ValueKind == JsonValueKind.String && v.GetString()!.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }
        if (f.CategoryId is int cid) q = q.Where(r => r.CategoryId == cid);
        if (f.From is DateOnly from) q = q.Where(r => r.Date >= from);
        if (f.To is DateOnly to) q = q.Where(r => r.Date <= to);
        if (!string.IsNullOrEmpty(f.Status)) q = q.Where(r => r.Status == f.Status);
        if (!string.IsNullOrEmpty(f.Kind)) q = q.Where(r => r.Kind == f.Kind);
        if (f.Additional is bool add) q = q.Where(r => r.IsAdditional == add);
        if (!string.IsNullOrEmpty(f.PaymentMethod)) q = q.Where(r => r.PaymentMethod == f.PaymentMethod);

        var desc = string.Equals(f.Dir, "desc", StringComparison.OrdinalIgnoreCase);
        q = (f.Sort ?? "date").ToLowerInvariant() switch
        {
            "amount" => desc ? q.OrderByDescending(r => r.Amount) : q.OrderBy(r => r.Amount),
            "category" => desc ? q.OrderByDescending(r => r.CategoryName) : q.OrderBy(r => r.CategoryName),
            "description" => desc ? q.OrderByDescending(r => r.Description) : q.OrderBy(r => r.Description),
            "status" => desc ? q.OrderByDescending(r => r.Status) : q.OrderBy(r => r.Status),
            "method" => desc ? q.OrderByDescending(r => r.PaymentMethod) : q.OrderBy(r => r.PaymentMethod),
            _ => desc ? q.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id) : q.OrderBy(r => r.Date).ThenBy(r => r.Id)
        };
        return q.ToList();
    }

    // Same rules as the calculator: only paid expenses and refunds move totals; planned and transfers are shown apart.
    public static LedgerTotalsDto Totals(IReadOnlyCollection<ExpenseRowDto> rows)
    {
        var paid = rows.Where(r => r.Status == TxStatus.Paid).ToList();
        var gross = paid.Where(r => r.Kind == TxKind.Expense).Sum(r => r.Amount);
        var refunds = paid.Where(r => r.Kind == TxKind.Refund).Sum(r => r.Amount);
        return new LedgerTotalsDto
        {
            Count = rows.Count,
            Gross = gross,
            Refunds = refunds,
            Net = gross - refunds,
            Planned = rows.Where(r => r.Status == TxStatus.Planned && r.Kind == TxKind.Expense).Sum(r => r.Amount),
            Transfers = rows.Where(r => r.Kind == TxKind.Transfer).Sum(r => r.Amount),
            Additional = paid.Where(r => r.Kind == TxKind.Expense && r.IsAdditional).Sum(r => r.Amount)
        };
    }

    public static ExpenseRowDto ToDto(Transaction t, IReadOnlyDictionary<int, decimal>? refunded = null) => new()
    {
        Id = t.Id,
        Version = t.Version,
        Date = DateOnly.FromDateTime(t.TransactionDate),
        Amount = t.Amount,
        Kind = t.Kind,
        Status = t.Status,
        CategoryId = t.CategoryId,
        CategoryName = t.Category?.Name ?? (t.Kind == TxKind.Transfer ? "Transfer" : string.Empty),
        CategoryColor = t.Category?.Color ?? "#898781",
        Description = t.Description,
        PaymentMethod = t.PaymentMethod,
        IsAdditional = t.IsAdditional,
        IsRecurring = t.RecurringOccurrenceId != null,
        RecurringOccurrenceId = t.RecurringOccurrenceId,
        RefundOfId = t.RefundOfId,
        RefundedAmount = refunded?.GetValueOrDefault(t.Id) ?? 0,
        AccountId = t.AccountId,
        AccountName = t.Account?.Name ?? string.Empty,
        TransferAccountId = t.TransferAccountId,
        TransferAccountName = t.TransferAccount?.Name,
        Custom = CustomValues.Read(t.CustomValuesJson)
    };

    public async Task<ExpenseRowDto?> GetAsync(int userId, int id)
    {
        var t = await Owned(userId).Include(x => x.Category).Include(x => x.Account).Include(x => x.TransferAccount)
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return t == null ? null : ToDto(t);
    }

    // ---------- Writes ----------

    public async Task<ServiceResult<ExpenseRowDto>> CreateAsync(int userId, ExpenseInputDto dto)
    {
        if (dto.ClientRequestId is Guid requestId)
        {
            var existing = await Owned(userId).AsNoTracking().FirstOrDefaultAsync(t => t.ClientRequestId == requestId);
            if (existing != null) return ServiceResult<ExpenseRowDto>.Ok((await GetAsync(userId, existing.Id))!);
        }

        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        var t = new Transaction { UserId = userId, CreatedAt = DateTime.UtcNow, ClientRequestId = dto.ClientRequestId };
        var error = await ApplyAsync(user, t, dto, isNew: true);
        if (error != null) return ServiceResult<ExpenseRowDto>.Fail(error);

        _db.Transactions.Add(t);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } && dto.ClientRequestId != null)
        {
            // The same request arrived twice at once; the first one won.
            _db.ChangeTracker.Clear();
            var first = await Owned(userId).AsNoTracking().FirstAsync(x => x.ClientRequestId == dto.ClientRequestId);
            return ServiceResult<ExpenseRowDto>.Ok((await GetAsync(userId, first.Id))!);
        }
        return ServiceResult<ExpenseRowDto>.Ok((await GetAsync(userId, t.Id))!);
    }

    public async Task<ServiceResult<ExpenseRowDto>> UpdateAsync(int userId, int id, ExpenseInputDto dto)
    {
        var t = await Owned(userId).FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return ServiceResult<ExpenseRowDto>.Fail("Not found", 404);
        if (dto.Version is not uint version) return ServiceResult<ExpenseRowDto>.Fail("Missing version; reload and try again.");

        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        var error = await ApplyAsync(user, t, dto, isNew: false);
        if (error != null) return ServiceResult<ExpenseRowDto>.Fail(error);
        return await SaveVersionedAsync(userId, t, version);
    }

    public async Task<ServiceResult<ExpenseRowDto>> PatchAsync(int userId, int id, ExpensePatchDto patch)
    {
        var t = await Owned(userId).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return ServiceResult<ExpenseRowDto>.Fail("Not found", 404);

        var custom = CustomValues.Read(t.CustomValuesJson);
        if (patch.Custom != null)
            foreach (var (key, value) in patch.Custom) custom[key] = value;

        var input = new ExpenseInputDto
        {
            Date = patch.Date ?? DateOnly.FromDateTime(t.TransactionDate),
            Amount = patch.Amount ?? t.Amount,
            Kind = t.Kind,
            Status = patch.Status ?? t.Status,
            CategoryId = patch.CategoryId ?? t.CategoryId,
            Description = patch.Description ?? t.Description,
            PaymentMethod = patch.PaymentMethod ?? t.PaymentMethod,
            IsAdditional = patch.IsAdditional ?? t.IsAdditional,
            AccountId = t.AccountId,
            TransferAccountId = t.TransferAccountId,
            RefundOfId = t.RefundOfId,
            Custom = custom,
            Version = patch.Version
        };
        return await UpdateAsync(userId, id, input);
    }

    private async Task<ServiceResult<ExpenseRowDto>> SaveVersionedAsync(int userId, Transaction t, uint version)
    {
        _db.Entry(t).Property(x => x.Version).OriginalValue = version;
        t.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return ServiceResult<ExpenseRowDto>.Fail("This entry was changed on another device. Your edit wasn't saved; the latest version is shown.",
                409, await GetAsync(userId, t.Id));
        }
        return ServiceResult<ExpenseRowDto>.Ok((await GetAsync(userId, t.Id))!);
    }

    public async Task<ServiceResult<bool>> DeleteAsync(int userId, int id, uint? version)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var t = await Owned(userId).FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return ServiceResult<bool>.Fail("Not found", 404);
        if (await Owned(userId).AnyAsync(r => r.RefundOfId == id))
            return ServiceResult<bool>.Fail("This expense has refunds. Delete the refunds first so totals stay consistent.", 409);

        if (version is uint v) _db.Entry(t).Property(x => x.Version).OriginalValue = v;

        // A paid recurring occurrence goes back to planned rather than disappearing.
        if (t.RecurringOccurrenceId is int occurrenceId)
        {
            var occurrence = await _db.RecurringOccurrences.FirstOrDefaultAsync(o => o.Id == occurrenceId && o.UserId == userId);
            if (occurrence != null)
            {
                occurrence.Status = OccurrenceStatus.Planned;
                occurrence.TransactionId = null;
            }
        }

        _db.Transactions.Remove(t);
        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<bool>.Fail("This entry was changed on another device. Reload before deleting.", 409);
        }
        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<ExpenseRowDto>> DuplicateAsync(int userId, int id, DuplicateDto dto)
    {
        var source = await Owned(userId).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (source == null) return ServiceResult<ExpenseRowDto>.Fail("Not found", 404);
        if (source.Kind == TxKind.Refund) return ServiceResult<ExpenseRowDto>.Fail("Refunds can't be duplicated; add a new refund instead.");

        return await CreateAsync(userId, new ExpenseInputDto
        {
            ClientRequestId = dto.ClientRequestId,
            Date = dto.Date ?? DateOnly.FromDateTime(source.TransactionDate),
            Amount = source.Amount,
            Kind = source.Kind,
            Status = source.Status,
            CategoryId = source.CategoryId,
            Description = source.Description,
            PaymentMethod = source.PaymentMethod,
            IsAdditional = source.IsAdditional,
            AccountId = source.AccountId,
            TransferAccountId = source.TransferAccountId,
            Custom = CustomValues.Read(source.CustomValuesJson)
        });
    }

    public Task<ServiceResult<ExpenseRowDto>> RefundAsync(int userId, int id, RefundInputDto dto) =>
        CreateAsync(userId, new ExpenseInputDto
        {
            ClientRequestId = dto.ClientRequestId,
            Kind = TxKind.Refund,
            Status = TxStatus.Paid,
            RefundOfId = id,
            Date = dto.Date,
            Amount = dto.Amount,
            Description = dto.Description
        });

    // Validates and copies input onto the entity. Returns an error message or null.
    private async Task<string?> ApplyAsync(User user, Transaction t, ExpenseInputDto dto, bool isNew)
    {
        var today = LocalCalendar.Today(_clock, user.TimeZone);

        if (!Money.IsValidAmount(dto.Amount)) return "Amount must be greater than zero with at most 2 decimals.";
        if (dto.Date == default) return "Date is required.";
        if (dto.Date.Year is < 2000 or > 3000) return "Date is out of range.";
        if (!isNew && t.Kind != dto.Kind) return "An entry's type can't be changed; delete it and add a new one.";
        if (dto.Status == TxStatus.Paid && dto.Date > today)
            return $"A paid entry can't be dated after today ({today:dd MMM yyyy}). Mark it planned instead.";
        if (dto.PaymentMethod.Length > 0 && !PaymentMethods.All.Contains(dto.PaymentMethod))
            return $"Payment method must be one of: {string.Join(", ", PaymentMethods.All)}.";

        Account account;
        if (dto.AccountId is int accountId)
        {
            var chosen = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == user.Id);
            if (chosen == null) return "Invalid account.";
            account = chosen;
        }
        else
        {
            account = await DefaultData.GetOrCreateDefaultAccountAsync(_db, user);
        }

        t.Kind = dto.Kind;
        t.Status = dto.Status;
        t.TransactionDate = dto.Date.ToDateTime(TimeOnly.MinValue);
        t.Amount = dto.Amount;
        t.Description = dto.Description.Trim();
        t.PaymentMethod = dto.PaymentMethod;
        t.AccountId = account.Id;

        switch (dto.Kind)
        {
            case TxKind.Expense:
            {
                if (dto.CategoryId is not int categoryId) return "Choose a category.";
                var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId && c.UserId == user.Id);
                if (category == null || category.Type != "Expense") return "Choose a spending category.";
                if (category.IsArchived && (isNew || t.CategoryId != categoryId)) return $"“{category.Name}” is archived; restore it to add entries.";
                t.CategoryId = categoryId;
                t.IsAdditional = dto.IsAdditional;
                t.RefundOfId = null;
                t.TransferAccountId = null;
                break;
            }
            case TxKind.Refund:
            {
                if (dto.RefundOfId is not int originalId) return "A refund must be linked to the original expense.";
                var original = await Owned(user.Id).AsNoTracking().FirstOrDefaultAsync(x => x.Id == originalId);
                if (original == null || original.Kind != TxKind.Expense || original.Status != TxStatus.Paid)
                    return "Refunds can only be linked to a paid expense.";
                if (dto.Status != TxStatus.Paid) return "A refund is recorded when it's received.";
                if (dto.Date < DateOnly.FromDateTime(original.TransactionDate)) return "A refund can't be dated before the original expense.";
                var otherRefunds = await Owned(user.Id).Where(x => x.RefundOfId == originalId && x.Id != t.Id).SumAsync(x => x.Amount);
                if (otherRefunds + dto.Amount > original.Amount)
                    return $"Refunds can't exceed the original {original.Amount:N2} ({otherRefunds:N2} already refunded).";
                t.RefundOfId = originalId;
                t.CategoryId = original.CategoryId;
                t.IsAdditional = false;
                t.TransferAccountId = null;
                break;
            }
            case TxKind.Transfer:
            {
                if (dto.TransferAccountId is not int toId) return "Choose the account the money moved to.";
                if (toId == account.Id) return "A transfer needs two different accounts.";
                if (!await _db.Accounts.AnyAsync(a => a.Id == toId && a.UserId == user.Id)) return "Invalid destination account.";
                t.TransferAccountId = toId;
                t.CategoryId = null;
                t.IsAdditional = false;
                t.RefundOfId = null;
                break;
            }
            default:
                return "Type must be Expense, Refund or Transfer.";
        }

        if (dto.Custom != null)
        {
            var (json, error) = await _columns.NormalizeValuesAsync(user.Id, dto.Custom);
            if (error != null) return error;
            t.CustomValuesJson = json!;
        }
        return null;
    }

    // ---------- Daily matrix ----------

    public async Task<MatrixDto> MatrixAsync(int userId, YearMonth month)
    {
        var user = await _loader.UserAsync(userId);
        var today = LocalCalendar.Today(_clock, user.TimeZone);
        var start = month.FirstDay;
        var end = month.NextFirstDay;

        var rows = await Owned(userId)
            .Where(t => t.TransactionDate >= start && t.TransactionDate < end && t.CategoryId != null
                && (t.Kind == TxKind.Expense || t.Kind == TxKind.Refund))
            .OrderBy(t => t.Id)
            .AsNoTracking()
            .ToListAsync();

        var used = rows.Select(r => r.CategoryId!.Value).ToHashSet();
        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.UserId == userId && c.Type == "Expense")
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync();
        categories = categories.Where(c => !c.IsArchived || used.Contains(c.Id)).ToList();

        var layout = await _columns.LayoutAsync(userId, "daily", month);
        var order = layout.Columns.Select((c, i) => (c.Key, i)).ToDictionary(x => x.Key, x => x.i);
        var settings = layout.Columns.ToDictionary(c => c.Key);
        var ordered = categories
            .OrderBy(c => order.TryGetValue($"cat:{c.Id}", out var i) ? i : int.MaxValue)
            .ThenBy(c => c.SortOrder)
            .Select(c => new MatrixCategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                Color = c.Color,
                GroupName = c.GroupName,
                IsArchived = c.IsArchived,
                Hidden = settings.TryGetValue($"cat:{c.Id}", out var s) && s.Hidden,
                Width = settings.TryGetValue($"cat:{c.Id}", out var w) ? w.Width : null
            })
            .ToList();

        var startDate = DateOnly.FromDateTime(start);
        var endDate = DateOnly.FromDateTime(end);
        var notes = await _db.DayNotes.AsNoTracking()
            .Where(n => n.UserId == userId && n.Date >= startDate && n.Date < endDate)
            .ToDictionaryAsync(n => n.Date, n => n.Text);

        var byDay = rows.ToLookup(r => DateOnly.FromDateTime(r.TransactionDate));
        var dto = new MatrixDto { Month = month, Currency = user.Currency, Today = today, LayoutSource = layout.Source, Categories = ordered };
        foreach (var c in ordered) dto.ColumnTotals[c.Id] = 0;

        for (var day = 1; day <= month.DaysInMonth; day++)
        {
            var date = new DateOnly(month.Year, month.Month, day);
            var row = new MatrixDayDto { Date = date, Note = notes.GetValueOrDefault(date, string.Empty) };
            foreach (var group in byDay[date].GroupBy(t => t.CategoryId!.Value))
            {
                var cell = new MatrixCellDto
                {
                    Entries = group.Select(t => new MatrixEntryDto
                    {
                        Id = t.Id, Version = t.Version, Amount = t.Amount, Description = t.Description, Status = t.Status,
                        Kind = t.Kind, PaymentMethod = t.PaymentMethod, IsAdditional = t.IsAdditional
                    }).ToList()
                };
                cell.Net = group.Where(t => t.Status == TxStatus.Paid).Sum(t => t.Kind == TxKind.Refund ? -t.Amount : t.Amount);
                cell.Planned = group.Where(t => t.Status == TxStatus.Planned && t.Kind == TxKind.Expense).Sum(t => t.Amount);
                row.Cells[group.Key] = cell;
                row.Net += cell.Net;
                dto.ColumnTotals[group.Key] = dto.ColumnTotals.GetValueOrDefault(group.Key) + cell.Net;
            }
            dto.Net += row.Net;
            dto.Days.Add(row);
        }
        return dto;
    }

    public async Task SetNoteAsync(int userId, DateOnly date, string text)
    {
        var note = await _db.DayNotes.FirstOrDefaultAsync(n => n.UserId == userId && n.Date == date);
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            if (note != null) _db.DayNotes.Remove(note);
        }
        else if (note == null)
        {
            _db.DayNotes.Add(new DayNote { UserId = userId, Date = date, Text = trimmed });
        }
        else
        {
            note.Text = trimmed;
        }
        await _db.SaveChangesAsync();
    }
}
