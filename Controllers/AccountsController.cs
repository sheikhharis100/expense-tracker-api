using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/accounts")]
[Authorize]
public class AccountsController : ControllerBase
{
    private readonly AppDbContext _context;

    public AccountsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<ActionResult<AccountResponseDto>> CreateAccount(AccountCreateDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);

        var nameTaken = await _context.Accounts
            .AnyAsync(a => a.UserId == userId && a.Name.ToLower() == dto.Name.ToLower());
        if (nameTaken)
            return BadRequest("An account with this name already exists");

        var account = new Account
        {
            UserId = userId,
            Name = dto.Name,
            Type = dto.Type,
            Currency = dto.Currency,
            OpeningBalance = dto.OpeningBalance
        };

        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAccount), new { id = account.Id }, MapToResponse(account));
    }

    [HttpGet]
    public async Task<ActionResult<List<AccountResponseDto>>> GetAccounts()
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var accounts = await _context.Accounts
            .Where(a => a.UserId == userId)
            .ToListAsync();

        return Ok(accounts.Select(MapToResponse).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AccountResponseDto>> GetAccount(int id)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var account = await _context.Accounts
            .Include(a => a.Transactions)
                .ThenInclude(t => t.Category)
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

        if (account == null)
            return NotFound();

        var response = MapToResponse(account);
        response.Transactions = account.Transactions
            .Select(t => new TransactionResponseDto
            {
                Id = t.Id,
                AccountId = t.AccountId,
                AccountName = account.Name,
                CategoryId = t.CategoryId,
                CategoryName = t.Category.Name,
                Amount = t.Amount,
                Description = t.Description,
                TransactionDate = t.TransactionDate,
                CreatedAt = t.CreatedAt
            })
            .OrderByDescending(t => t.TransactionDate)
            .Take(10)
            .ToList();

        return Ok(response);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAccount(int id, AccountUpdateDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

        if (account == null)
            return NotFound();

        var nameTaken = await _context.Accounts
            .AnyAsync(a => a.UserId == userId && a.Id != id && a.Name.ToLower() == dto.Name.ToLower());
        if (nameTaken)
            return BadRequest("An account with this name already exists");

        account.Name = dto.Name;
        account.Type = dto.Type;
        account.Currency = dto.Currency;
        account.OpeningBalance = dto.OpeningBalance;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAccount(int id)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var account = await _context.Accounts
            .Include(a => a.Transactions)
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

        if (account == null)
            return NotFound();

        if (account.Transactions.Any())
        {
            return BadRequest("Cannot delete account with existing transactions. Delete transactions first.");
        }

        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static AccountResponseDto MapToResponse(Account account)
    {
        return new AccountResponseDto
        {
            Id = account.Id,
            Name = account.Name,
            Type = account.Type,
            Currency = account.Currency,
            OpeningBalance = account.OpeningBalance
        };
    }
}