using Microsoft.AspNetCore.Mvc;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

[Route("api/summary")]
public class SummaryController : AppController
{
    private readonly SummaryService _summary;

    public SummaryController(SummaryService summary)
    {
        _summary = summary;
    }

    [HttpGet("{month}")]
    public async Task<ActionResult<MonthSummaryDto>> Month(string month) =>
        YearMonth.TryParse(month, out var ym) ? Ok(await _summary.MonthAsync(UserId, ym)) : BadMonth();

    [HttpGet("year/{year:int}")]
    public async Task<ActionResult<AnnualSummaryDto>> Year(int year) =>
        year is >= 2000 and <= 3000 ? Ok(await _summary.YearAsync(UserId, year)) : Error("Year out of range");
}

[Route("api/months/{month}")]
public class MonthsController : AppController
{
    private readonly MonthService _months;
    private readonly ColumnService _columns;
    private readonly BudgetService _budgets;
    private readonly RecurringService _recurring;
    private readonly LedgerNotifier _notifier;

    public MonthsController(MonthService months, ColumnService columns, BudgetService budgets, RecurringService recurring, LedgerNotifier notifier)
    {
        _months = months;
        _columns = columns;
        _budgets = budgets;
        _recurring = recurring;
        _notifier = notifier;
    }

    [HttpGet("settings")]
    public async Task<ActionResult<MonthSettingDto>> GetSettings(string month) =>
        YearMonth.TryParse(month, out var ym) ? Ok(await _months.GetAsync(UserId, ym)) : BadMonth();

    [HttpPut("settings")]
    public async Task<ActionResult> SaveSettings(string month, MonthSettingInputDto dto)
    {
        if (!YearMonth.TryParse(month, out var ym)) return BadMonth();
        var result = await _months.SaveAsync(UserId, ym, dto);
        if (result.Error == null) await _notifier.NotifyAsync(UserId, LedgerScopes.Settings, ym);
        return Result(result);
    }

    // Start a month from another one's setup: layouts, budget targets and recurring plans. Paid entries are never copied.
    [HttpPost("copy")]
    public async Task<ActionResult<MonthCopyResultDto>> Copy(string month, MonthCopyDto dto)
    {
        if (!YearMonth.TryParse(month, out var ym)) return BadMonth();
        if (dto.From == ym) return Error("Choose a different month to copy from.");
        var result = new MonthCopyResultDto();
        if (dto.Layout) result.LayoutsCopied = await _columns.CopyLayoutsAsync(UserId, dto.From, ym);
        if (dto.Budgets) result.BudgetsCopied = await _budgets.CopyAsync(UserId, dto.From, ym);
        if (dto.Recurring)
        {
            await _recurring.EnsureOccurrencesAsync(UserId, ym, ym);
            result.OccurrencesPlanned = await _recurring.CountPlannedAsync(UserId, ym);
        }
        await _notifier.NotifyAsync(UserId, LedgerScopes.Settings, ym);
        return Ok(result);
    }
}

[Route("api/budgets")]
public class BudgetsController : AppController
{
    private readonly BudgetService _budgets;
    private readonly LedgerNotifier _notifier;

    public BudgetsController(BudgetService budgets, LedgerNotifier notifier)
    {
        _budgets = budgets;
        _notifier = notifier;
    }

    [HttpGet("{month}")]
    public async Task<ActionResult<BudgetMonthDto>> Month(string month) =>
        YearMonth.TryParse(month, out var ym) ? Ok(await _budgets.MonthAsync(UserId, ym)) : BadMonth();

    [HttpPut("{month}/{categoryId:int}")]
    public async Task<ActionResult> Set(string month, int categoryId, BudgetSetDto dto)
    {
        if (!YearMonth.TryParse(month, out var ym)) return BadMonth();
        var error = await _budgets.SetAsync(UserId, categoryId, ym, dto.Amount, dto.Scope);
        if (error != null) return Error(error);
        await _notifier.NotifyAsync(UserId, LedgerScopes.Budgets, ym);
        return Ok(await _budgets.MonthAsync(UserId, ym));
    }
}

[Route("api/exports")]
public class ExportsController : AppController
{
    private readonly ExportService _exports;

    public ExportsController(ExportService exports)
    {
        _exports = exports;
    }

    // format: xlsx | csv. part (csv only): summary | entries | income | categories. basis: budget-month | cash-flow.
    [HttpGet("month/{month}")]
    public async Task<ActionResult> Month(string month, [FromQuery] string format = "xlsx", [FromQuery] string part = "summary", [FromQuery] string basis = "budget-month")
    {
        if (!YearMonth.TryParse(month, out var ym)) return BadMonth();
        if (format is not ("xlsx" or "csv")) return Error("format must be xlsx or csv");
        if (basis is not ("budget-month" or "cash-flow")) return Error("basis must be budget-month or cash-flow");
        var file = await _exports.MonthAsync(UserId, ym, format, part, basis);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("year/{year:int}")]
    public async Task<ActionResult> Year(int year, [FromQuery] string format = "xlsx")
    {
        if (format is not ("xlsx" or "csv")) return Error("format must be xlsx or csv");
        var file = await _exports.YearAsync(UserId, year, format);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
