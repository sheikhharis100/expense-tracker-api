using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/budgets")]
[Authorize]
public class BudgetsController : ControllerBase
{
    private readonly AppDbContext _context;

    public BudgetsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<ActionResult<BudgetResponseDto>> CreateBudget(BudgetCreateDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);

        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == dto.CategoryId && c.UserId == userId);
        if (category == null)
            return BadRequest("Invalid category");

        var existing = await _context.Budgets
            .FirstOrDefaultAsync(b => b.CategoryId == dto.CategoryId && b.Month == dto.Month && b.Year == dto.Year);

        if (existing != null)
            return BadRequest("Budget already exists for this category and month");

        var budget = new Budget
        {
            CategoryId = dto.CategoryId,
            MonthlyLimit = dto.MonthlyLimit,
            Month = dto.Month,
            Year = dto.Year
        };

        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetBudgets), new { month = dto.Month, year = dto.Year }, MapToResponse(budget, category.Name, 0, 0));
    }

    [HttpGet]
    public async Task<ActionResult<List<BudgetResponseDto>>> GetBudgets([FromQuery] int month, [FromQuery] int year)
    {
        var userId = AuthHelper.GetCurrentUserId(User);

        var budgets = await _context.Budgets
            .Where(b => b.Category.UserId == userId && b.Month == month && b.Year == year)
            .Include(b => b.Category)
            .ToListAsync();

        var transactions = await _context.Transactions
            .Where(t => t.Account.UserId == userId
                && t.TransactionDate.Month == month
                && t.TransactionDate.Year == year
                && t.Category.Type == "Expense")
            .ToListAsync();

        var results = budgets.Select(b =>
        {
            var spent = transactions.Where(t => t.CategoryId == b.CategoryId).Sum(t => t.Amount);
            var percentUsed = b.MonthlyLimit > 0 ? (spent / b.MonthlyLimit) * 100 : 0;
            return MapToResponse(b, b.Category.Name, spent, Math.Round(percentUsed, 2));
        }).ToList();

        return Ok(results);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<BudgetResponseDto>> GetBudget(int id)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var budget = await _context.Budgets
            .Include(b => b.Category)
            .FirstOrDefaultAsync(b => b.Id == id && b.Category.UserId == userId);

        if (budget == null)
            return NotFound();

        var transactions = await _context.Transactions
            .Where(t => t.Account.UserId == userId
                && t.TransactionDate.Month == budget.Month
                && t.TransactionDate.Year == budget.Year
                && t.CategoryId == budget.CategoryId
                && t.Category.Type == "Expense")
            .ToListAsync();

        var spent = transactions.Sum(t => t.Amount);
        var percentUsed = budget.MonthlyLimit > 0 ? (spent / budget.MonthlyLimit) * 100 : 0;

        return Ok(MapToResponse(budget, budget.Category.Name, spent, Math.Round(percentUsed, 2)));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBudget(int id, BudgetUpdateDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var budget = await _context.Budgets
            .Include(b => b.Category)
            .FirstOrDefaultAsync(b => b.Id == id && b.Category.UserId == userId);

        if (budget == null)
            return NotFound();

        budget.MonthlyLimit = dto.MonthlyLimit;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBudget(int id)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var budget = await _context.Budgets
            .Include(b => b.Category)
            .FirstOrDefaultAsync(b => b.Id == id && b.Category.UserId == userId);

        if (budget == null)
            return NotFound();

        _context.Budgets.Remove(budget);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static BudgetResponseDto MapToResponse(Budget budget, string categoryName, decimal spent, decimal percentUsed)
    {
        return new BudgetResponseDto
        {
            Id = budget.Id,
            CategoryId = budget.CategoryId,
            CategoryName = categoryName,
            MonthlyLimit = budget.MonthlyLimit,
            CurrentSpend = spent,
            Month = budget.Month,
            Year = budget.Year,
            PercentUsed = percentUsed,
            IsOverBudget = spent > budget.MonthlyLimit
        };
    }
}