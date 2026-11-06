using Microsoft.AspNetCore.Mvc;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

[Route("api/income")]
public class IncomeController : AppController
{
    private readonly IncomeService _income;
    private readonly LedgerNotifier _notifier;

    public IncomeController(IncomeService income, LedgerNotifier notifier)
    {
        _income = income;
        _notifier = notifier;
    }

    // Salary funding this budget month (and its payments), salary earned this month, and other income.
    [HttpGet("{month}")]
    public async Task<ActionResult<IncomeMonthDto>> Month(string month) =>
        YearMonth.TryParse(month, out var ym) ? Ok(await _income.MonthAsync(UserId, ym)) : BadMonth();

    [HttpGet("plans")]
    public async Task<ActionResult<List<SalaryPlanDto>>> Plans() => Ok(await _income.PlansAsync(UserId));

    [HttpPut("plans")]
    public async Task<ActionResult> SavePlan(SalaryPlanInputDto dto) => await Changed(await _income.UpsertPlanAsync(UserId, dto));

    [HttpDelete("plans/{id:int}")]
    public async Task<ActionResult> DeletePlan(int id)
    {
        if (!await _income.DeletePlanAsync(UserId, id)) return NotFound(new { error = "Not found" });
        await _notifier.NotifyAsync(UserId, LedgerScopes.Salary);
        return NoContent();
    }

    // Saves the salary earned in {earned}: its components and the budget month it funds.
    [HttpPut("statements/{earned}")]
    public async Task<ActionResult> SaveStatement(string earned, SalaryStatementInputDto dto)
    {
        if (!YearMonth.TryParse(earned, out var ym)) return BadMonth();
        var result = await _income.SaveStatementAsync(UserId, ym, dto);
        if (result.Error != null) return Result(result);
        await _notifier.NotifyAsync(UserId, LedgerScopes.Salary);
        return Ok(new { id = result.Value!.Id });
    }

    [HttpPost("statements/{earned}/payments")]
    public async Task<ActionResult> AddPayment(string earned, ReceiptInputDto dto)
    {
        if (!YearMonth.TryParse(earned, out var ym)) return BadMonth();
        return await Changed(await _income.AddSalaryPaymentAsync(UserId, ym, dto));
    }

    [HttpPost("receipts")]
    public async Task<ActionResult> AddOther(ReceiptInputDto dto) => await Changed(await _income.AddOtherIncomeAsync(UserId, dto));

    [HttpPut("receipts/{id:int}")]
    public async Task<ActionResult> UpdateReceipt(int id, ReceiptInputDto dto) => await Changed(await _income.UpdateReceiptAsync(UserId, id, dto));

    [HttpDelete("receipts/{id:int}")]
    public async Task<ActionResult> DeleteReceipt(int id)
    {
        var result = await _income.DeleteReceiptAsync(UserId, id);
        if (result.Error != null) return Result(result);
        await _notifier.NotifyAsync(UserId, LedgerScopes.Salary);
        return NoContent();
    }

    private async Task<ActionResult> Changed<T>(ServiceResult<T> result)
    {
        if (result.Error == null) await _notifier.NotifyAsync(UserId, LedgerScopes.Salary);
        return Result(result);
    }
}
