using Microsoft.AspNetCore.Mvc;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

[Route("api/expenses")]
public class ExpensesController : AppController
{
    private const long MaxImportBytes = 5 * 1024 * 1024;

    private readonly ExpenseService _expenses;
    private readonly AnalyticsService _analytics;
    private readonly CsvImportService _csv;
    private readonly SheetImportService _sheet;
    private readonly LedgerNotifier _notifier;

    public ExpensesController(ExpenseService expenses, AnalyticsService analytics, CsvImportService csv, SheetImportService sheet, LedgerNotifier notifier)
    {
        _expenses = expenses;
        _analytics = analytics;
        _csv = csv;
        _sheet = sheet;
        _notifier = notifier;
    }

    [HttpGet]
    public async Task<ActionResult<ExpenseListDto>> List([FromQuery] ExpenseFilterDto filter) =>
        YearMonth.TryParse(filter.Month, out var ym) ? Ok(await _expenses.ListAsync(UserId, ym, filter)) : BadMonth();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ExpenseRowDto>> Get(int id) =>
        await _expenses.GetAsync(UserId, id) is { } row ? Ok(row) : NotFound(new { error = "Not found" });

    [HttpPost]
    public async Task<ActionResult> Create(ExpenseInputDto dto) => await Changed(await _expenses.CreateAsync(UserId, dto));

    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, ExpenseInputDto dto) => await Changed(await _expenses.UpdateAsync(UserId, id, dto));

    [HttpPatch("{id:int}")]
    public async Task<ActionResult> Patch(int id, ExpensePatchDto dto) => await Changed(await _expenses.PatchAsync(UserId, id, dto));

    [HttpPost("{id:int}/duplicate")]
    public async Task<ActionResult> Duplicate(int id, DuplicateDto dto) => await Changed(await _expenses.DuplicateAsync(UserId, id, dto));

    [HttpPost("{id:int}/refunds")]
    public async Task<ActionResult> Refund(int id, RefundInputDto dto) => await Changed(await _expenses.RefundAsync(UserId, id, dto));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, [FromQuery] uint? version)
    {
        var before = await _expenses.GetAsync(UserId, id);
        var result = await _expenses.DeleteAsync(UserId, id, version);
        if (result.Error != null) return Result(result);
        if (before != null) await _notifier.NotifyAsync(UserId, LedgerScopes.Entries, YearMonth.Of(before.Date));
        return NoContent();
    }

    private async Task<ActionResult> Changed(ServiceResult<ExpenseRowDto> result)
    {
        if (result.Value is { } row)
        {
            await _notifier.NotifyAsync(UserId, LedgerScopes.Entries, YearMonth.Of(row.Date));
            if (row.CategoryId is int categoryId)
                await _analytics.CheckBudgetAlertAsync(UserId, categoryId, row.Date.Month, row.Date.Year);
        }
        return Result(result);
    }

    // ---------- Daily matrix (same transactions, dates × categories) ----------

    [HttpGet("matrix/{month}")]
    public async Task<ActionResult<MatrixDto>> Matrix(string month) =>
        YearMonth.TryParse(month, out var ym) ? Ok(await _expenses.MatrixAsync(UserId, ym)) : BadMonth();

    [HttpPut("notes")]
    public async Task<ActionResult> Note(DayNoteUpdateDto dto)
    {
        await _expenses.SetNoteAsync(UserId, dto.Date, dto.Text);
        await _notifier.NotifyAsync(UserId, LedgerScopes.Notes, YearMonth.Of(dto.Date));
        return NoContent();
    }

    // ---------- Imports ----------

    // Google Sheets → Daily Log tab → File → Download → CSV.
    [HttpPost("import/sheet")]
    [RequestSizeLimit(MaxImportBytes)]
    public async Task<ActionResult<SheetImportResultDto>> ImportSheet(IFormFile file)
    {
        if (file == null || file.Length == 0) return Error("No file uploaded");
        using var stream = file.OpenReadStream();
        var result = await _sheet.ImportAsync(UserId, stream);
        if (result.DaysImported > 0 || result.SalariesImported > 0) await _notifier.NotifyAsync(UserId, LedgerScopes.Import);
        return Ok(result);
    }

    // CSV with Date, Description, Amount, Category, Account columns (the original import format).
    [HttpPost("import/csv")]
    [RequestSizeLimit(MaxImportBytes)]
    public async Task<ActionResult<CsvImportResponseDto>> ImportCsv(IFormFile file)
    {
        if (file == null || file.Length == 0) return Error("No file uploaded");
        using var stream = file.OpenReadStream();
        var result = await _csv.ImportTransactionsAsync(UserId, stream);
        if (result.ImportedSuccessfully > 0) await _notifier.NotifyAsync(UserId, LedgerScopes.Import);
        return Ok(new CsvImportResponseDto
        {
            TotalRows = result.TotalRows,
            ImportedSuccessfully = result.ImportedSuccessfully,
            Skipped = result.Skipped,
            Errors = result.Errors.Select(e => new CsvImportErrorDto { Row = e.Row, Reason = e.Reason }).ToList()
        });
    }
}

[Route("api/columns")]
public class ColumnsController : AppController
{
    private readonly ColumnService _columns;
    private readonly LedgerNotifier _notifier;

    public ColumnsController(ColumnService columns, LedgerNotifier notifier)
    {
        _columns = columns;
        _notifier = notifier;
    }

    [HttpGet]
    public async Task<ActionResult<List<ColumnDto>>> List() => Ok((await _columns.ColumnsAsync(UserId)).Select(c => c.Dto).ToList());

    [HttpPost("check")]
    public async Task<ActionResult<FormulaCheckResultDto>> Check(FormulaCheckDto dto) => Ok(await _columns.CheckFormulaAsync(UserId, dto.Formula, dto.ColumnId));

    [HttpPost]
    public async Task<ActionResult> Create(ColumnInputDto dto) => await Changed(await _columns.CreateAsync(UserId, dto));

    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, ColumnInputDto dto) => await Changed(await _columns.UpdateAsync(UserId, id, dto));

    // mode: omitted → 409 with the impact when the column holds data; "archive" keeps values; "delete" removes them.
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Remove(int id, [FromQuery] string? mode)
    {
        if (mode is not (null or "archive" or "delete")) return Error("mode must be archive or delete");
        var result = await _columns.RemoveAsync(UserId, id, mode);
        if (result.Error == null) await _notifier.NotifyAsync(UserId, LedgerScopes.Columns);
        return result.Error == null ? NoContent() : Result(result);
    }

    [HttpPost("{id:int}/restore")]
    public async Task<ActionResult> Restore(int id)
    {
        var result = await _columns.RestoreAsync(UserId, id);
        if (result.Error == null) await _notifier.NotifyAsync(UserId, LedgerScopes.Columns);
        return result.Error == null ? NoContent() : Result(result);
    }

    [HttpGet("layout/{table}/{month}")]
    public async Task<ActionResult<LayoutDto>> Layout(string table, string month) =>
        YearMonth.TryParse(month, out var ym) ? Ok(await _columns.LayoutAsync(UserId, table, ym)) : BadMonth();

    [HttpPut("layout/{table}/{month}")]
    public async Task<ActionResult> SaveLayout(string table, string month, LayoutUpdateDto dto)
    {
        if (!YearMonth.TryParse(month, out var ym)) return BadMonth();
        var result = await _columns.SaveLayoutAsync(UserId, table, ym, dto);
        if (result.Error == null) await _notifier.NotifyAsync(UserId, LedgerScopes.Columns, ym);
        return Result(result);
    }

    private async Task<ActionResult> Changed(ServiceResult<ColumnDto> result)
    {
        if (result.Error == null) await _notifier.NotifyAsync(UserId, LedgerScopes.Columns);
        return Result(result);
    }
}
