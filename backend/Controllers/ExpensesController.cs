using Microsoft.AspNetCore.Mvc;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

[Route("api/expenses")]
public class ExpensesController : AppController
{
    public const long MaxImportBytes = 5 * 1024 * 1024;
    // Room for the multipart wrapping around the file, so a file just under the limit still gets through.
    private const long MultipartAllowance = 64 * 1024;

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
    public async Task<ActionResult<ExpenseListDto>> List([FromQuery] ExpenseFilterDto filter)
    {
        if (!YearMonth.TryParse(filter.Month, out var ym)) return BadMonth();
        if (ExpenseService.FilterError(filter) is { } error) return Error(error);
        return Ok(await _expenses.ListAsync(UserId, ym, filter));
    }

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
        if (await _expenses.SetNoteAsync(UserId, dto.Date, dto.Text) is { } error) return Error(error);
        await _notifier.NotifyAsync(UserId, LedgerScopes.Notes, YearMonth.Of(dto.Date));
        return NoContent();
    }

    // ---------- Imports ----------

    // Google Sheets → Daily Log tab → File → Download → CSV.
    [HttpPost("import/sheet")]
    [RequestSizeLimit(MaxImportBytes + MultipartAllowance)]
    public async Task<ActionResult<SheetImportResultDto>> ImportSheet(IFormFile? file)
    {
        if (await FileError(file) is { } error) return Error(error);
        using var stream = file!.OpenReadStream();
        var result = await _sheet.ImportAsync(UserId, stream);
        if (result.DaysImported > 0 || result.SalariesImported > 0) await _notifier.NotifyAsync(UserId, LedgerScopes.Import);
        return Ok(result);
    }

    // CSV with Date, Description, Amount, Category, Account columns (the original import format).
    [HttpPost("import/csv")]
    [RequestSizeLimit(MaxImportBytes + MultipartAllowance)]
    public async Task<ActionResult<CsvImportResponseDto>> ImportCsv(IFormFile? file)
    {
        if (await FileError(file) is { } error) return Error(error);
        using var stream = file!.OpenReadStream();
        var result = await _csv.ImportTransactionsAsync(UserId, stream);
        if (result.Error != null) return Error(result.Error);
        if (result.ImportedSuccessfully > 0) await _notifier.NotifyAsync(UserId, LedgerScopes.Import);
        return Ok(new CsvImportResponseDto
        {
            TotalRows = result.TotalRows,
            ImportedSuccessfully = result.ImportedSuccessfully,
            Skipped = result.Skipped,
            Errors = result.Errors.Select(e => new CsvImportErrorDto { Row = e.Row, Reason = e.Reason }).ToList()
        });
    }

    // Imports take a .csv export under 5 MB. A spreadsheet renamed to .csv is binary (an .xlsx is a zip), so a
    // NUL byte near the start gives it away.
    private static async Task<string?> FileError(IFormFile? file)
    {
        if (file == null || file.Length == 0) return "Choose a .csv file to import.";
        if (file.Length > MaxImportBytes) return $"The file is too large; the limit is {MaxImportBytes / (1024 * 1024)} MB. Export just the tab you need.";
        var extension = Path.GetExtension(file.FileName);
        var csvType = file.ContentType?.StartsWith("text/csv", StringComparison.OrdinalIgnoreCase) == true;
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) && !csvType)
            return "Upload a .csv file (in Google Sheets: File → Download → Comma-separated values).";

        var head = new byte[Math.Min(8192, file.Length)];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(head);
        return head.AsSpan(0, read).IndexOf((byte)0) >= 0
            ? "That file isn't a CSV. In Google Sheets use File → Download → Comma-separated values."
            : null;
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
    public async Task<ActionResult<LayoutDto>> Layout(string table, string month)
    {
        if (table is not (ColumnService.ExpensesTable or ColumnService.DailyTable)) return NotFound(new { error = "Unknown table" });
        return YearMonth.TryParse(month, out var ym) ? Ok(await _columns.LayoutAsync(UserId, table, ym)) : BadMonth();
    }

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
