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
    private readonly CurrencyConverter _fx;

    public AccountsController(AppDbContext context, CurrencyConverter fx)
    {
        _context = context;
        _fx = fx;
    }

    [HttpPost]
    public async Task<ActionResult<AccountResponseDto>> CreateAccount(AccountCreateDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var name = InputRules.Clean(dto.Name);
        var currency = dto.Currency.Trim().ToUpperInvariant();
        if (await ValidateAsync(userId, null, name, currency, dto.OpeningBalance) is { } error)
            return Error(error);

        var account = new Account
        {
            UserId = userId,
            Name = name,
            Type = dto.Type,
            Currency = currency,
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
            .OrderByDescending(t => t.TransactionDate)
            .Take(10)
            .Select(t => ExpenseService.ToDto(t))
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

        var name = InputRules.Clean(dto.Name);
        var currency = dto.Currency.Trim().ToUpperInvariant();
        if (await ValidateAsync(userId, id, name, currency, dto.OpeningBalance) is { } error)
            return Error(error);

        account.Name = name;
        account.Type = dto.Type;
        account.Currency = currency;
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

        if (account.Transactions.Any() || await _context.Transactions.AnyAsync(t => t.TransferAccountId == id))
        {
            return Error("Cannot delete account with existing transactions. Delete transactions first.");
        }

        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // Name required and unique (ignoring case), a supported currency, and an opening balance that fits.
    private async Task<string?> ValidateAsync(int userId, int? id, string name, string currency, decimal openingBalance)
    {
        if (InputRules.Name(name, InputRules.NameLength) is { } nameError) return nameError;
        if (!_fx.SupportedCurrencies.Contains(currency))
            return $"Currency must be one of: {string.Join(", ", _fx.SupportedCurrencies)}.";
        if (InputRules.Signed(openingBalance, "Opening balance") is { } balanceError) return balanceError;
        var taken = await _context.Accounts
            .AnyAsync(a => a.UserId == userId && a.Id != id && a.Name.ToLower() == name.ToLower());
        return taken ? $"There's already an account called “{name}”." : null;
    }

    private BadRequestObjectResult Error(string message) => BadRequest(new { error = message });

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
