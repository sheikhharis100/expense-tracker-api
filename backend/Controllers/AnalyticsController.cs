using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _analyticsService;

    public AnalyticsController(AnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    private static bool IsValidMonth(int month, int year) => YearMonth.IsValid(year, month);

    private ActionResult BadMonth() =>
        BadRequest(new { error = $"month must be 1–12 and year between {InputRules.MinYear} and {InputRules.MaxYear}" });

    [HttpGet("by-category")]
    public async Task<ActionResult<List<SpendingByCategoryDto>>> GetSpendingByCategory([FromQuery] int month, [FromQuery] int year)
    {
        if (!IsValidMonth(month, year)) return BadMonth();
        var userId = AuthHelper.GetCurrentUserId(User);
        var result = await _analyticsService.GetSpendingByCategoryAsync(userId, month, year);
        return Ok(result);
    }

    [HttpGet("trend")]
    public async Task<ActionResult<List<TrendDto>>> GetSpendingTrend([FromQuery] int months = 6)
    {
        if (months is < 1 or > 120) return BadRequest(new { error = "months must be between 1 and 120" });
        var userId = AuthHelper.GetCurrentUserId(User);
        var result = await _analyticsService.GetSpendingTrendAsync(userId, months);
        return Ok(result);
    }

    [HttpGet("budget-status")]
    public async Task<ActionResult<List<BudgetStatusDto>>> GetBudgetStatus([FromQuery] int month, [FromQuery] int year)
    {
        if (!IsValidMonth(month, year)) return BadMonth();
        var userId = AuthHelper.GetCurrentUserId(User);
        var result = await _analyticsService.GetBudgetStatusAsync(userId, month, year);
        return Ok(result);
    }

    [HttpGet("alerts")]
    public async Task<ActionResult<List<BudgetAlertResponseDto>>> GetBudgetAlerts()
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var result = await _analyticsService.GetBudgetAlertsAsync(userId);
        return Ok(result);
    }

    [HttpPut("alerts/{alertId}/read")]
    public async Task<IActionResult> MarkAlertAsRead(int alertId)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        await _analyticsService.MarkAlertAsReadAsync(userId, alertId);
        return NoContent();
    }
}