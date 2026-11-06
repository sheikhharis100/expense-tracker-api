using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Ledger;

// Reads one user's records into calculator inputs. No figures are computed here.
public class LedgerLoader
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly CurrencyConverter _fx;

    public LedgerLoader(AppDbContext db, IClock clock, CurrencyConverter fx)
    {
        _db = db;
        _clock = clock;
        _fx = fx;
    }

    public async Task<User> UserAsync(int userId) => await _db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

    public async Task<DateOnly> TodayAsync(int userId)
    {
        var zone = await _db.Users.Where(u => u.Id == userId).Select(u => u.TimeZone).FirstAsync();
        return LocalCalendar.Today(_clock, zone);
    }

    public async Task<LedgerData> LoadAsync(int userId, YearMonth from, YearMonth to)
    {
        var user = await UserAsync(userId);
        var today = LocalCalendar.Today(_clock, user.TimeZone);

        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new LedgerCategory(c.Id, c.Name, c.Type, c.Color, c.GroupName, c.IsArchived, c.SortOrder))
            .ToListAsync();

        var chainStart = await ChainStartAsync(userId);
        var loadFrom = user.CarryoverEnabled && chainStart is YearMonth cs && cs < from ? cs : from;
        var fromDate = loadFrom.FirstDay;
        var toDate = to.NextFirstDay;

        var txRows = await _db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && t.Kind != TxKind.LegacyIncome && t.TransactionDate >= fromDate && t.TransactionDate < toDate)
            .Select(t => new
            {
                t.Id, t.TransactionDate, t.Amount, t.CategoryId, t.Kind, t.Status, t.IsAdditional, t.RefundOfId,
                RefundOfIsAdditional = t.RefundOf != null && t.RefundOf.IsAdditional,
                t.Account.Currency
            })
            .ToListAsync();

        var transactions = txRows
            .Select(t => new LedgerTxn(t.Id, DateOnly.FromDateTime(t.TransactionDate), _fx.Convert(t.Amount, t.Currency, user.Currency),
                t.CategoryId, t.Kind, t.Status, t.IsAdditional, t.RefundOfId, t.RefundOfIsAdditional))
            .ToList();

        var receipts = (await _db.IncomeReceipts.AsNoTracking()
                .Where(r => r.UserId == userId)
                .Include(r => r.Allocations)
                .ToListAsync())
            .Select(r => new LedgerReceipt(r.Id, r.Kind, r.Status, r.ReceivedDate, r.Amount, r.SalaryStatementId, r.CategoryId,
                r.Allocations.Select(a => new LedgerAllocation(new YearMonth(a.Year, a.Month), a.Amount)).ToList()))
            .ToList();

        var statements = await StatementsFundingAsync(user, loadFrom, to);

        var occurrences = await _db.RecurringOccurrences.AsNoTracking()
            .Where(o => o.UserId == userId && o.Status == OccurrenceStatus.Planned
                && o.Year * 12 + o.Month - 1 >= loadFrom.Index && o.Year * 12 + o.Month - 1 <= to.Index)
            .Select(o => new { o.Id, o.DueDate, o.Amount, o.RecurringItem.CategoryId, o.RecurringItem.Description, o.RecurringItem.Kind, o.Year, o.Month })
            .ToListAsync();

        // A planned transaction created for an occurrence and the occurrence itself are one commitment, not two.
        var linkedOccurrenceIds = await _db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && t.RecurringOccurrenceId != null)
            .Select(t => t.RecurringOccurrenceId!.Value)
            .ToListAsync();
        var linked = linkedOccurrenceIds.ToHashSet();

        var commitments = occurrences
            .Where(o => o.Kind == TxKind.Expense && !linked.Contains(o.Id))
            .Select(o => new LedgerCommitment("recurring", o.Id, o.DueDate, o.Amount, o.CategoryId, o.Description))
            .Concat(transactions
                .Where(t => t.Kind == TxKind.Expense && t.Status == TxStatus.Planned)
                .Select(t => new LedgerCommitment("planned", t.Id, t.Date, t.Amount, t.CategoryId,
                    categories.FirstOrDefault(c => c.Id == t.CategoryId)?.Name ?? "Planned expense")))
            .ToList();

        // Recurring income that hasn't arrived is expected income for its month (plan only).
        receipts.AddRange(occurrences
            .Where(o => o.Kind == "Income")
            .Select(o => new LedgerReceipt(-o.Id, ReceiptKind.Other, ReceiptStatus.Expected, null, o.Amount, null, o.CategoryId,
                new[] { new LedgerAllocation(new YearMonth(o.Year, o.Month), o.Amount) })));

        var settings = await _db.MonthSettings.AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToDictionaryAsync(s => new YearMonth(s.Year, s.Month), s => new LedgerMonthSetting(new YearMonth(s.Year, s.Month), s.OpeningBalance, s.ReservedSavings));

        return new LedgerData
        {
            Today = today,
            Carryover = user.CarryoverEnabled,
            Categories = categories,
            Transactions = transactions,
            Receipts = receipts,
            Statements = statements,
            Budgets = await BudgetsAsync(userId, loadFrom, to),
            Commitments = commitments,
            Settings = settings,
            ChainStart = chainStart
        };
    }

    // Earliest month holding money: a dated transaction, an income allocation, or a hand-entered opening balance.
    public async Task<YearMonth?> ChainStartAsync(int userId)
    {
        var firstTx = await _db.Transactions.Where(t => t.UserId == userId && t.Kind != TxKind.LegacyIncome)
            .MinAsync(t => (DateTime?)t.TransactionDate);
        var firstAlloc = await _db.IncomeAllocations.Where(a => a.Receipt.UserId == userId && a.Receipt.Status == ReceiptStatus.Received)
            .OrderBy(a => a.Year).ThenBy(a => a.Month).Select(a => new { a.Year, a.Month }).FirstOrDefaultAsync();
        var firstOpening = await _db.MonthSettings.Where(s => s.UserId == userId && s.OpeningBalance != null)
            .OrderBy(s => s.Year).ThenBy(s => s.Month).Select(s => new { s.Year, s.Month }).FirstOrDefaultAsync();

        var candidates = new List<YearMonth>();
        if (firstTx.HasValue) candidates.Add(YearMonth.Of(firstTx.Value));
        if (firstAlloc != null) candidates.Add(new YearMonth(firstAlloc.Year, firstAlloc.Month));
        if (firstOpening != null) candidates.Add(new YearMonth(firstOpening.Year, firstOpening.Month));
        return candidates.Count == 0 ? null : candidates.Min();
    }

    // Saved salary statements funding the range, plus planned (unsaved) ones derived from the salary plan.
    public async Task<List<LedgerStatement>> StatementsFundingAsync(User user, YearMonth from, YearMonth to)
    {
        var saved = await _db.SalaryStatements.AsNoTracking()
            .Where(s => s.UserId == user.Id)
            .Include(s => s.Components)
            .ToListAsync();
        var plans = await _db.SalaryPlans.AsNoTracking().Where(p => p.UserId == user.Id).ToListAsync();

        var result = saved
            .Where(s => new YearMonth(s.FundedYear, s.FundedMonth) >= from && new YearMonth(s.FundedYear, s.FundedMonth) <= to)
            .Select(ToLedger)
            .ToList();

        var savedEarned = saved.Select(s => new YearMonth(s.EarnedYear, s.EarnedMonth)).ToHashSet();
        for (var funded = from; funded <= to; funded = funded.AddMonths(1))
        {
            var earned = funded.AddMonths(-user.SalaryLagMonths);
            if (savedEarned.Contains(earned)) continue;
            var planned = PlannedStatement(plans, earned, funded);
            if (planned != null) result.Add(planned);
        }
        return result;
    }

    public static LedgerStatement ToLedger(SalaryStatement s) => new(
        s.Id,
        new YearMonth(s.EarnedYear, s.EarnedMonth),
        new YearMonth(s.FundedYear, s.FundedMonth),
        s.Components.OrderBy(c => c.SortOrder).Select(c => new LedgerComponent(c.Kind, c.Label, c.Amount)).ToList(),
        false);

    public static LedgerStatement? PlannedStatement(IEnumerable<SalaryPlan> plans, YearMonth earned, YearMonth funded)
    {
        var plan = plans.Where(p => new YearMonth(p.Year, p.Month) <= earned)
            .OrderByDescending(p => p.Year * 12 + p.Month)
            .FirstOrDefault();
        if (plan == null || plan.Amount + plan.Allowances <= 0) return null;

        var components = new List<LedgerComponent> { new(SalaryComponentKind.Base, "Base salary", plan.Amount) };
        if (plan.Allowances != 0) components.Add(new LedgerComponent(SalaryComponentKind.Allowance, "Allowances", plan.Allowances));
        return new LedgerStatement(null, earned, funded, components, true);
    }

    // Explicit month budget wins; otherwise the latest template at or before the month. Missing = not set.
    public async Task<Dictionary<(int, YearMonth), decimal>> BudgetsAsync(int userId, YearMonth from, YearMonth to)
    {
        var explicitRows = await _db.Budgets.AsNoTracking()
            .Where(b => b.Category.UserId == userId)
            .Select(b => new { b.CategoryId, b.Year, b.Month, b.MonthlyLimit })
            .ToListAsync();
        var templates = await _db.BudgetTemplates.AsNoTracking()
            .Where(b => b.Category.UserId == userId)
            .Select(b => new { b.CategoryId, b.EffectiveYear, b.EffectiveMonth, b.MonthlyLimit })
            .ToListAsync();

        var result = new Dictionary<(int, YearMonth), decimal>();
        var categoryIds = explicitRows.Select(b => b.CategoryId).Concat(templates.Select(t => t.CategoryId)).Distinct();
        foreach (var categoryId in categoryIds)
        {
            for (var m = from; m <= to; m = m.AddMonths(1))
            {
                var month = m;
                var row = explicitRows.FirstOrDefault(b => b.CategoryId == categoryId && b.Year == month.Year && b.Month == month.Month);
                if (row != null)
                {
                    result[(categoryId, month)] = row.MonthlyLimit;
                    continue;
                }
                var template = templates
                    .Where(t => t.CategoryId == categoryId && new YearMonth(t.EffectiveYear, t.EffectiveMonth) <= month)
                    .OrderByDescending(t => t.EffectiveYear * 12 + t.EffectiveMonth)
                    .FirstOrDefault();
                if (template?.MonthlyLimit is decimal limit)
                    result[(categoryId, month)] = limit;
            }
        }
        return result;
    }
}
