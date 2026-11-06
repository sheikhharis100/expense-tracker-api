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

    [HttpGet("by-category")]
    public async Task<ActionResult<List<SpendingByCategoryDto>>> GetSpendingByCategory([FromQuery] int month, [FromQuery] int year)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var result = await _analyticsService.GetSpendingByCategoryAsync(userId, month, year);
        return Ok(result);
    }

    [HttpGet("trend")]
    public async Task<ActionResult<List<TrendDto>>> GetSpendingTrend([FromQuery] int months = 6)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var result = await _analyticsService.GetSpendingTrendAsync(userId, months);
        return Ok(result);
    }

    [HttpGet("budget-status")]
    public async Task<ActionResult<List<BudgetStatusDto>>> GetBudgetStatus([FromQuery] int month, [FromQuery] int year)
    {
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