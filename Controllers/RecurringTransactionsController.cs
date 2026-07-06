using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/recurring-transactions")]
[Authorize]
public class RecurringTransactionsController : ControllerBase
{
    private readonly AppDbContext _context;

    public RecurringTransactionsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<ActionResult<RecurringTransactionResponseDto>> CreateRecurringTransaction(RecurringTransactionCreateDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == dto.AccountId && a.UserId == userId);
        if (account == null)
            return BadRequest("Invalid account");

        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == dto.CategoryId && c.UserId == userId);
        if (category == null)
            return BadRequest("Invalid category");

        var recurring = new RecurringTransaction
        {
            UserId = userId,
            AccountId = dto.AccountId,
            CategoryId = dto.CategoryId,
            Amount = dto.Amount,
            Description = dto.Description,
            DayOfMonth = dto.DayOfMonth,
            NextRunDate = ComputeNextRunDate(dto.DayOfMonth, DateTime.UtcNow.Date),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.RecurringTransactions.Add(recurring);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRecurringTransaction), new { id = recurring.Id }, MapToResponse(recurring, account.Name, category.Name));
    }

    [HttpGet]
    public async Task<ActionResult<List<RecurringTransactionResponseDto>>> GetRecurringTransactions()
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var recurring = await _context.RecurringTransactions
            .Include(r => r.Account)
            .Include(r => r.Category)
            .Where(r => r.UserId == userId)
            .ToListAsync();

        return Ok(recurring.Select(r => MapToResponse(r, r.Account.Name, r.Category.Name)).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RecurringTransactionResponseDto>> GetRecurringTransaction(int id)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var recurring = await _context.RecurringTransactions
            .Include(r => r.Account)
            .Include(r => r.Category)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

        if (recurring == null)
            return NotFound();

        return Ok(MapToResponse(recurring, recurring.Account.Name, recurring.Category.Name));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateRecurringTransaction(int id, RecurringTransactionUpdateDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var recurring = await _context.RecurringTransactions
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

        if (recurring == null)
            return NotFound();

        recurring.Amount = dto.Amount;
        recurring.Description = dto.Description;
        recurring.IsActive = dto.IsActive;

        if (recurring.DayOfMonth != dto.DayOfMonth)
        {
            recurring.DayOfMonth = dto.DayOfMonth;
            recurring.NextRunDate = ComputeNextRunDate(dto.DayOfMonth, DateTime.UtcNow.Date);
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRecurringTransaction(int id)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var recurring = await _context.RecurringTransactions
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

        if (recurring == null)
            return NotFound();

        _context.RecurringTransactions.Remove(recurring);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static DateTime ComputeNextRunDate(int dayOfMonth, DateTime fromDate)
    {
        var candidate = new DateTime(fromDate.Year, fromDate.Month, dayOfMonth);
        return candidate < fromDate ? candidate.AddMonths(1) : candidate;
    }

    private static RecurringTransactionResponseDto MapToResponse(RecurringTransaction recurring, string accountName, string categoryName)
    {
        return new RecurringTransactionResponseDto
        {
            Id = recurring.Id,
            AccountId = recurring.AccountId,
            AccountName = accountName,
            CategoryId = recurring.CategoryId,
            CategoryName = categoryName,
            Amount = recurring.Amount,
            Description = recurring.Description,
            DayOfMonth = recurring.DayOfMonth,
            NextRunDate = recurring.NextRunDate,
            IsActive = recurring.IsActive
        };
    }
}
