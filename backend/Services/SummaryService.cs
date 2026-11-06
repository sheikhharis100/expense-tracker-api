using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

// Month and year reports. Every figure comes from LedgerCalculator so cards, tables and exports agree.
public class SummaryService
{
    private readonly AppDbContext _db;
    private readonly LedgerLoader _loader;
    private readonly RecurringService _recurring;

    public SummaryService(AppDbContext db, LedgerLoader loader, RecurringService recurring)
    {
        _db = db;
        _loader = loader;
        _recurring = recurring;
    }

    public async Task<MonthSummaryDto> MonthAsync(int userId, YearMonth month)
    {
        await _recurring.EnsureOccurrencesAsync(userId, month, month);
        var user = await _loader.UserAsync(userId);
        var data = await _loader.LoadAsync(userId, month.AddMonths(-1), month);
        var range = LedgerCalculator.Range(data, month.AddMonths(-1), month);
        var previous = range[0];
        var current = range[1];

        return new MonthSummaryDto
        {
            Currency = user.Currency,
            Today = data.Today,
            Figures = current,
            Previous = new ComparisonDto
            {
                Month = previous.Month,
                ReceivedIncome = previous.ReceivedIncome,
                NetSpending = previous.NetSpending,
                RemainingBalance = previous.RemainingBalance,
                NetSpendingChange = current.NetSpending - previous.NetSpending,
                NetSpendingChangePercent = previous.NetSpending > 0 ? Money.Percent(current.NetSpending - previous.NetSpending, previous.NetSpending) : null,
                ReceivedIncomeChange = current.ReceivedIncome - previous.ReceivedIncome
            },
            CashFlow = LedgerCalculator.CashFlow(data, month)
        };
    }

    public async Task<(LedgerData Data, IReadOnlyList<MonthFigures> Months)> YearFiguresAsync(int userId, int year)
    {
        var from = new YearMonth(year, 1);
        var to = new YearMonth(year, 12);
        var today = await _loader.TodayAsync(userId);
        // Plan occurrences up to two years ahead at most.
        var generateTo = YearMonth.Of(today).AddMonths(24);
        if (from <= generateTo) await _recurring.EnsureOccurrencesAsync(userId, from, to < generateTo ? to : generateTo);

        var data = await _loader.LoadAsync(userId, from, to);
        return (data, LedgerCalculator.Range(data, from, to));
    }

    public async Task<AnnualSummaryDto> YearAsync(int userId, int year)
    {
        var user = await _loader.UserAsync(userId);
        var (_, months) = await YearFiguresAsync(userId, year);

        var rows = months.Select(Row).ToList();
        var categories = months.SelectMany(m => m.Categories)
            .GroupBy(c => c.CategoryId)
            .Select(g => new CategoryTotalDto { CategoryId = g.Key, Name = g.First().Name, Color = g.First().Color, Net = g.Sum(c => c.Net) })
            .Where(c => c.Net != 0)
            .OrderByDescending(c => c.Net)
            .ToList();

        return new AnnualSummaryDto
        {
            Year = year,
            Currency = user.Currency,
            Rows = rows,
            Totals = new AnnualRowDto
            {
                ExpectedSalary = rows.Sum(r => r.ExpectedSalary),
                SalaryIncome = rows.Sum(r => r.SalaryIncome),
                OtherIncome = rows.Sum(r => r.OtherIncome),
                ReceivedIncome = rows.Sum(r => r.ReceivedIncome),
                OpeningBalance = rows.FirstOrDefault()?.OpeningBalance ?? 0,
                GrossSpending = rows.Sum(r => r.GrossSpending),
                Refunds = rows.Sum(r => r.Refunds),
                NetSpending = rows.Sum(r => r.NetSpending),
                AdditionalNet = rows.Sum(r => r.AdditionalNet),
                Surplus = rows.Sum(r => r.Surplus),
                RemainingBalance = rows.LastOrDefault()?.RemainingBalance ?? 0,
                IncomeUsedPercent = rows.Sum(r => r.ReceivedIncome) > 0 ? Money.Percent(rows.Sum(r => r.NetSpending), rows.Sum(r => r.ReceivedIncome)) : null,
                UnpaidCommitments = rows.Sum(r => r.UnpaidCommitments)
            },
            Categories = categories
        };
    }

    public static AnnualRowDto Row(MonthFigures m) => new()
    {
        Month = m.Month,
        Timing = m.Timing,
        ExpectedSalary = m.ExpectedSalary,
        SalaryIncome = m.SalaryIncome,
        OtherIncome = m.OtherIncome,
        ReceivedIncome = m.ReceivedIncome,
        OpeningBalance = m.OpeningBalance,
        GrossSpending = m.GrossSpending,
        Refunds = m.Refunds,
        NetSpending = m.NetSpending,
        AdditionalNet = m.AdditionalNet,
        Surplus = m.Surplus,
        RemainingBalance = m.RemainingBalance,
        IncomeUsedPercent = m.IncomeUsedPercent,
        TotalBudget = m.TotalBudget,
        UnpaidCommitments = m.UnpaidCommitments
    };
}

public class BudgetService
{
    private readonly AppDbContext _db;
    private readonly LedgerLoader _loader;

    public BudgetService(AppDbContext db, LedgerLoader loader)
    {
        _db = db;
        _loader = loader;
    }

    public async Task<BudgetMonthDto> MonthAsync(int userId, YearMonth month)
    {
        var data = await _loader.LoadAsync(userId, month, month);
        var figures = LedgerCalculator.Single(data, month);
        var explicitIds = await _db.Budgets.Where(b => b.Category.UserId == userId && b.Year == month.Year && b.Month == month.Month)
            .Select(b => b.CategoryId).ToListAsync();

        return new BudgetMonthDto
        {
            Month = month,
            Lines = figures.Categories.Select(c => new BudgetLineDto
            {
                CategoryId = c.CategoryId,
                Name = c.Name,
                Color = c.Color,
                IsArchived = c.IsArchived,
                Amount = c.Budget,
                Source = c.Budget == null ? "none" : explicitIds.Contains(c.CategoryId) ? "month" : "template",
                Net = c.Net,
                Remaining = c.BudgetRemaining
            }).ToList(),
            TotalBudget = figures.TotalBudget,
            OverallRemaining = figures.OverallBudgetRemaining,
            UnbudgetedSpending = figures.UnbudgetedSpending,
            UnbudgetedCategories = figures.UnbudgetedCategories.ToList(),
            CoverageWarning = figures.BudgetCoverageWarning
        };
    }

    // scope "month": only this month. scope "future": this month and every later month (later one-month
    // budgets are replaced). Earlier months keep their budgets either way.
    public async Task<string?> SetAsync(int userId, int categoryId, YearMonth month, decimal? amount, string scope)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId && c.UserId == userId);
        if (category == null || category.Type != "Expense") return "Choose a spending category.";
        if (amount is decimal a && InputRules.NonNegative(a, "Budget") is { } problem) return problem;

        await using var tx = await _db.Database.BeginTransactionAsync();
        if (scope == "month")
        {
            var row = await _db.Budgets.FirstOrDefaultAsync(b => b.CategoryId == categoryId && b.Year == month.Year && b.Month == month.Month);
            if (amount is not decimal value)
            {
                if (row != null) _db.Budgets.Remove(row);
            }
            else
            {
                if (row == null)
                {
                    row = new Budget { CategoryId = categoryId, Year = month.Year, Month = month.Month };
                    _db.Budgets.Add(row);
                }
                row.MonthlyLimit = value;
            }
        }
        else
        {
            var template = await _db.BudgetTemplates.FirstOrDefaultAsync(b => b.CategoryId == categoryId && b.EffectiveYear == month.Year && b.EffectiveMonth == month.Month);
            if (template == null)
            {
                template = new BudgetTemplate { CategoryId = categoryId, EffectiveYear = month.Year, EffectiveMonth = month.Month };
                _db.BudgetTemplates.Add(template);
            }
            template.MonthlyLimit = amount;
            _db.BudgetTemplates.RemoveRange(await _db.BudgetTemplates
                .Where(b => b.CategoryId == categoryId && b.EffectiveYear * 12 + b.EffectiveMonth - 1 > month.Index).ToListAsync());
            _db.Budgets.RemoveRange(await _db.Budgets
                .Where(b => b.CategoryId == categoryId && b.Year * 12 + b.Month - 1 >= month.Index).ToListAsync());
        }
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return null;
    }

    // Copies budgets in effect for `from` onto `to` as one-month budgets.
    public async Task<int> CopyAsync(int userId, YearMonth from, YearMonth to)
    {
        var budgets = await _loader.BudgetsAsync(userId, from, from);
        foreach (var ((categoryId, _), amount) in budgets)
        {
            var row = await _db.Budgets.FirstOrDefaultAsync(b => b.CategoryId == categoryId && b.Year == to.Year && b.Month == to.Month);
            if (row == null)
            {
                row = new Budget { CategoryId = categoryId, Year = to.Year, Month = to.Month };
                _db.Budgets.Add(row);
            }
            row.MonthlyLimit = amount;
        }
        await _db.SaveChangesAsync();
        return budgets.Count;
    }
}

public class MonthService
{
    private readonly AppDbContext _db;

    public MonthService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<MonthSettingDto> GetAsync(int userId, YearMonth month)
    {
        var carryover = await _db.Users.Where(u => u.Id == userId).Select(u => u.CarryoverEnabled).FirstAsync();
        var s = await _db.MonthSettings.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId && m.Year == month.Year && m.Month == month.Month);
        return new MonthSettingDto
        {
            Month = month,
            OpeningBalance = s?.OpeningBalance,
            ReservedSavings = s?.ReservedSavings ?? 0,
            Note = s?.Note ?? string.Empty,
            CarryoverEnabled = carryover,
            Version = s?.Version ?? 0
        };
    }

    public async Task<ServiceResult<MonthSettingDto>> SaveAsync(int userId, YearMonth month, MonthSettingInputDto dto)
    {
        var note = InputRules.Clean(dto.Note);
        var problem = InputRules.NonNegative(dto.ReservedSavings, "Reserved savings")
            ?? (dto.OpeningBalance is decimal o ? InputRules.Signed(o, "Opening balance") : null)
            ?? InputRules.Text(note, InputRules.NoteLength, "Note");
        if (problem != null) return ServiceResult<MonthSettingDto>.Fail(problem);

        var s = await _db.MonthSettings.FirstOrDefaultAsync(m => m.UserId == userId && m.Year == month.Year && m.Month == month.Month);
        if (s == null)
        {
            s = new MonthSetting { UserId = userId, Year = month.Year, Month = month.Month };
            _db.MonthSettings.Add(s);
        }
        else if (dto.Version is uint v)
        {
            _db.Entry(s).Property(x => x.Version).OriginalValue = v;
        }
        s.OpeningBalance = dto.OpeningBalance;
        s.ReservedSavings = dto.ReservedSavings;
        s.Note = note;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<MonthSettingDto>.Fail("These settings were changed on another device. Reload and try again.", 409);
        }
        return ServiceResult<MonthSettingDto>.Ok(await GetAsync(userId, month));
    }
}
