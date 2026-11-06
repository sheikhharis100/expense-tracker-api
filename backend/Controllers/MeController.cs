using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

[Route("api/me")]
public class MeController : AppController
{
    private readonly AppDbContext _context;
    private readonly CurrencyConverter _fx;
    private readonly IClock _clock;
    private readonly LedgerNotifier _notifier;

    public MeController(AppDbContext context, CurrencyConverter fx, IClock clock, LedgerNotifier notifier)
    {
        _context = context;
        _fx = fx;
        _clock = clock;
        _notifier = notifier;
    }

    [HttpGet]
    public async Task<ActionResult<MeResponseDto>> GetMe()
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == UserId);
        if (user == null) return Unauthorized();

        if (!await _context.Categories.AnyAsync(c => c.UserId == user.Id))
            await DefaultData.SeedAsync(_context, user);

        return Ok(await MapAsync(user));
    }

    [HttpPut]
    public async Task<ActionResult<MeResponseDto>> UpdateMe(MeUpdateDto dto)
    {
        var user = await _context.Users.FirstAsync(u => u.Id == UserId);

        var currency = dto.Currency.ToUpperInvariant();
        if (!_fx.SupportedCurrencies.Contains(currency))
            return Error($"Unsupported currency. Use one of: {string.Join(", ", _fx.SupportedCurrencies)}");
        if (!LocalCalendar.IsValidZone(dto.TimeZone))
            return Error("Unknown time zone. Use an IANA name such as Asia/Karachi.");
        if (dto.DefaultAccountId is int accountId && !await _context.Accounts.AnyAsync(a => a.Id == accountId && a.UserId == user.Id))
            return Error("Invalid account");

        user.DisplayName = dto.DisplayName.Trim();
        user.Currency = currency;
        user.TimeZone = dto.TimeZone;
        user.SalaryLagMonths = dto.SalaryLagMonths;
        user.CarryoverEnabled = dto.CarryoverEnabled;
        user.DefaultAccountId = dto.DefaultAccountId ?? user.DefaultAccountId;

        await _context.SaveChangesAsync();
        await _notifier.NotifyAsync(user.Id, LedgerScopes.Profile);
        return Ok(await MapAsync(user));
    }

    private async Task<MeResponseDto> MapAsync(User user)
    {
        var today = LocalCalendar.Today(_clock, user.TimeZone);
        return new MeResponseDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Currency = user.Currency,
            TimeZone = user.TimeZone,
            SalaryLagMonths = user.SalaryLagMonths,
            CarryoverEnabled = user.CarryoverEnabled,
            DefaultAccountId = user.DefaultAccountId,
            Today = today,
            CurrentMonth = YearMonth.Of(today),
            SupportedCurrencies = _fx.SupportedCurrencies.ToList(),
            PaymentMethods = PaymentMethods.All.ToList(),
            NeedsSetup = !await _context.SalaryPlans.AnyAsync(s => s.UserId == user.Id)
                && !await _context.SalaryStatements.AnyAsync(s => s.UserId == user.Id),
            EmailVerified = user.EmailVerifiedAt != null,
            MemberSince = LocalCalendar.ToLocalDate(user.CreatedAt, user.TimeZone)
        };
    }
}
