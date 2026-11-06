using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

// One-time upgrade of records written by the first version, run at startup after migrations, per user,
// inside a database transaction. Assumptions (also in README "Upgrading existing data"):
//  1. The first version counted the planned salary as received in every budget month up to today. Those months
//     get a saved salary statement plus one received payment dated the 1st of the budget month, so past totals
//     are unchanged. Future months stay expected only.
//  2. A month's "Salary adjust" becomes an Adjustment line on the salary funding that month (and part of
//     the payment when the month is not in the future). Its note moves to the month's settings.
//  3. Entries in income categories (e.g. "Pending / extra") become received other-income receipts on the same
//     date, allocated to that month. The original rows are kept as LegacyIncome and excluded from all totals.
//  4. Starter categories that are missing are added; nothing is renamed or removed.
public class LegacyBackfillService
{
    public const int CurrentVersion = 2;

    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<LegacyBackfillService> _logger;

    public LegacyBackfillService(AppDbContext db, IClock clock, ILogger<LegacyBackfillService> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var userIds = await _db.Users.Where(u => u.DataVersion < CurrentVersion).Select(u => u.Id).ToListAsync();
        foreach (var id in userIds)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                await UpgradeUserAsync(id);
                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _db.ChangeTracker.Clear();
                _logger.LogError(ex, "Backfill failed for user {UserId}; their data was left unchanged", id);
            }
        }
    }

    public async Task UpgradeUserAsync(int userId)
    {
        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        var today = LocalCalendar.Today(_clock, user.TimeZone);
        var currentMonth = YearMonth.Of(today);

        await DefaultData.SeedAsync(_db, user);

        // 3. Income-category entries → receipts.
        var incomeRows = await _db.Transactions
            .Where(t => t.UserId == userId && t.Kind == TxKind.Expense && t.Category != null && t.Category.Type == "Income")
            .ToListAsync();
        foreach (var t in incomeRows)
        {
            var date = DateOnly.FromDateTime(t.TransactionDate);
            _db.IncomeReceipts.Add(new IncomeReceipt
            {
                UserId = userId,
                Kind = ReceiptKind.Other,
                CategoryId = t.CategoryId,
                Status = ReceiptStatus.Received,
                ReceivedDate = date,
                Amount = t.Amount,
                Description = string.IsNullOrWhiteSpace(t.Description) ? "Moved from the earlier version" : t.Description,
                CreatedAt = DateTime.UtcNow,
                Allocations = { new IncomeAllocation { Year = date.Year, Month = date.Month, Amount = t.Amount } }
            });
            t.Kind = TxKind.LegacyIncome;
        }

        // 1 & 2. Salary history and adjustments → statements and receipts.
        var plans = await _db.SalaryPlans.AsNoTracking().Where(p => p.UserId == userId).ToListAsync();
        var adjustments = await _db.MonthAdjustments.AsNoTracking().Where(a => a.UserId == userId).ToListAsync();
        var existingEarned = (await _db.SalaryStatements.Where(s => s.UserId == userId).Select(s => new { s.EarnedYear, s.EarnedMonth }).ToListAsync())
            .Select(s => new YearMonth(s.EarnedYear, s.EarnedMonth)).ToHashSet();

        var fundedMonths = new SortedSet<YearMonth>(adjustments.Select(a => new YearMonth(a.Year, a.Month)));
        if (plans.Count > 0)
        {
            var firstFunded = plans.Select(p => new YearMonth(p.Year, p.Month)).Min().AddMonths(user.SalaryLagMonths);
            for (var m = firstFunded; m <= currentMonth && fundedMonths.Count < 600; m = m.AddMonths(1))
                fundedMonths.Add(m);
        }

        foreach (var funded in fundedMonths)
        {
            var earned = funded.AddMonths(-user.SalaryLagMonths);
            if (existingEarned.Contains(earned)) continue;

            var components = LedgerLoader.PlannedStatement(plans, earned, funded)?.Components.ToList() ?? new List<LedgerComponent>();
            var adjust = adjustments.FirstOrDefault(a => a.Year == funded.Year && a.Month == funded.Month);
            if (adjust != null && adjust.SalaryAdjust != 0)
                components.Add(new LedgerComponent(SalaryComponentKind.Adjustment, "Salary adjust", adjust.SalaryAdjust));
            if (components.Count == 0) continue;

            var statement = new SalaryStatement
            {
                UserId = userId,
                EarnedYear = earned.Year,
                EarnedMonth = earned.Month,
                FundedYear = funded.Year,
                FundedMonth = funded.Month,
                Note = "Created from the earlier salary history",
                CreatedAt = DateTime.UtcNow,
                Components = components.Select((c, i) => new SalaryComponent { Kind = c.Kind, Label = c.Label, Amount = c.Amount, SortOrder = i }).ToList()
            };
            _db.SalaryStatements.Add(statement);

            var net = SalaryMath.ExpectedNet(components);
            if (funded <= currentMonth && net > 0)
            {
                var paidOn = DateOnly.FromDateTime(funded.FirstDay);
                _db.IncomeReceipts.Add(new IncomeReceipt
                {
                    UserId = userId,
                    Kind = ReceiptKind.Salary,
                    SalaryStatement = statement,
                    Status = ReceiptStatus.Received,
                    ReceivedDate = paidOn > today ? today : paidOn,
                    Amount = net,
                    Description = "Recorded as received by the earlier version",
                    CreatedAt = DateTime.UtcNow,
                    Allocations = { new IncomeAllocation { Year = funded.Year, Month = funded.Month, Amount = net } }
                });
            }
            existingEarned.Add(earned);
        }

        foreach (var a in adjustments.Where(a => !string.IsNullOrWhiteSpace(a.Note)))
        {
            var setting = await _db.MonthSettings.FirstOrDefaultAsync(s => s.UserId == userId && s.Year == a.Year && s.Month == a.Month);
            if (setting == null)
            {
                setting = new MonthSetting { UserId = userId, Year = a.Year, Month = a.Month };
                _db.MonthSettings.Add(setting);
            }
            if (setting.Note.Length == 0) setting.Note = a.Note;
        }

        user.DataVersion = CurrentVersion;
        await _db.SaveChangesAsync();
    }
}
