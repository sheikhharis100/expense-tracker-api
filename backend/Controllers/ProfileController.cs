using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

// The signed-in person's account: name, email, password, sessions and deleting it all. Preferences (currency,
// salary rule…) stay in MeController.
[Route("api/me")]
public class ProfileController : AppController
{
    private const string WrongCurrentPassword = "Your current password isn't right.";

    private readonly AppDbContext _context;
    private readonly IClock _clock;
    private readonly PasswordPolicy _passwords;
    private readonly SessionTokens _sessions;
    private readonly OneTimeTokens _tokens;
    private readonly AccountEmails _emails;
    private readonly LedgerNotifier _notifier;

    public ProfileController(AppDbContext context, IClock clock, PasswordPolicy passwords, SessionTokens sessions,
        OneTimeTokens tokens, AccountEmails emails, LedgerNotifier notifier)
    {
        _context = context;
        _clock = clock;
        _passwords = passwords;
        _sessions = sessions;
        _tokens = tokens;
        _emails = emails;
        _notifier = notifier;
    }

    [HttpGet("stats")]
    public async Task<ActionResult<ProfileStatsDto>> Stats()
    {
        var user = await CurrentUserAsync();
        var entries = _context.Transactions.Where(t => t.UserId == user.Id && t.Kind != TxKind.LegacyIncome);
        var receipts = _context.IncomeReceipts.Where(r => r.UserId == user.Id);

        var starts = new List<YearMonth>();
        if (await entries.MinAsync(t => (DateTime?)t.TransactionDate) is DateTime firstEntry) starts.Add(YearMonth.Of(firstEntry));
        if (await receipts.MinAsync(r => r.ReceivedDate ?? r.ExpectedDate) is DateOnly firstReceipt) starts.Add(YearMonth.Of(firstReceipt));

        var thisMonth = YearMonth.Of(LocalCalendar.Today(_clock, user.TimeZone));
        YearMonth? first = starts.Count > 0 ? starts.Min() : null;
        return Ok(new ProfileStatsDto
        {
            EntriesLogged = await entries.CountAsync() + await receipts.CountAsync(),
            FirstMonth = first,
            // A first entry planned for a future month still counts as one month.
            MonthsTracked = first is YearMonth f ? Math.Max(1, thisMonth.Index - f.Index + 1) : 0
        });
    }

    [HttpPut("profile")]
    public async Task<ActionResult> UpdateProfile(ProfileUpdateRequest request)
    {
        var name = AccountRules.NormalizeName(request.DisplayName);
        if (AccountRules.CheckName(name) is string problem) return Error(problem);

        var user = await CurrentUserAsync();
        user.DisplayName = name;
        await _context.SaveChangesAsync();
        await _notifier.NotifyAsync(user.Id, LedgerScopes.Profile);
        return Ok(new { displayName = name });
    }

    // Signs out every other device; this one gets a fresh token in the response.
    [HttpPost("change-password")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<AuthResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await CurrentUserAsync();
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash)) return Error(WrongCurrentPassword);
        if (request.NewPassword == request.CurrentPassword) return Error("Choose a password that's different from your current one.");
        if (await _passwords.CheckAsync(request.NewPassword, user.Email, ct) is string problem) return Error(problem);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.SessionVersion++;
        await _context.SaveChangesAsync();
        SessionTokens.Forget(user.Id);
        // An older reset or sign-in link mustn't undo this.
        await _tokens.RevokeAsync(user.Id, AuthTokenPurpose.Reset, AuthTokenPurpose.Login);

        _emails.SendPasswordChangedNotice(user);
        await _notifier.NotifyAsync(user.Id, LedgerScopes.Session);
        return Ok(_sessions.Issue(user));
    }

    [HttpPost("change-email")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<AuthResponse>> ChangeEmail(ChangeEmailRequest request)
    {
        var user = await CurrentUserAsync();
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash)) return Error(WrongCurrentPassword);
        var email = AccountRules.NormalizeEmail(request.NewEmail);
        if (AccountRules.CheckEmail(email) is string problem) return Error(problem);
        if (email == user.Email) return Error("That's already your email.");
        if (await _context.Users.AnyAsync(u => u.Id != user.Id && u.Email.ToLower() == email))
            return Conflict(new { error = AccountRules.EmailTaken });

        var oldEmail = user.Email;
        user.Email = email;
        user.EmailVerifiedAt = null;
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = AccountRules.EmailTaken });
        }

        // Links already sent to the old address stop working.
        await _tokens.RevokeAsync(user.Id, AuthTokenPurpose.Reset, AuthTokenPurpose.Login, AuthTokenPurpose.Verify);
        _emails.SendEmailChangedNotice(user, oldEmail);
        await _emails.SendVerificationAsync(user);
        await _notifier.NotifyAsync(user.Id, LedgerScopes.Profile);
        // The token names the email, so this device gets one with the new address.
        return Ok(_sessions.Issue(user));
    }

    [HttpPost("verify-email/resend")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult> ResendVerification()
    {
        var user = await CurrentUserAsync();
        if (user.EmailVerifiedAt != null) return Error("Your email is already confirmed.");
        if (!_emails.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Email isn't set up on this server yet, so links can't be sent." });
        if (!await _emails.SendVerificationAsync(user))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "A link was sent less than a minute ago. Check your inbox and spam, or try again shortly." });
        return Ok(new { message = $"Link sent to {user.Email}. Check spam too." });
    }

    [HttpPost("sign-out-everywhere")]
    public async Task<ActionResult<AuthResponse>> SignOutEverywhere()
    {
        var user = await CurrentUserAsync();
        user.SessionVersion++;
        await _context.SaveChangesAsync();
        SessionTokens.Forget(user.Id);
        await _notifier.NotifyAsync(user.Id, LedgerScopes.Session);
        return Ok(_sessions.Issue(user));
    }

    [HttpDelete]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult> DeleteAccount(CurrentPasswordRequest request)
    {
        var user = await CurrentUserAsync();
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash)) return Error(WrongCurrentPassword);

        // Deleting the user cascades to everything they own. Rows that other rows point at through RESTRICT or
        // NO ACTION keys (refunds → expenses, payments → salary statements, entries and recurring items →
        // categories and accounts) go first, so the cascade never depends on the order Postgres happens to use.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.Transactions.Where(t => t.UserId == user.Id && t.RefundOfId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RefundOfId, (int?)null));
        await _context.Transactions.Where(t => t.UserId == user.Id).ExecuteDeleteAsync();
        await _context.IncomeReceipts.Where(r => r.UserId == user.Id).ExecuteDeleteAsync();
        await _context.RecurringItems.Where(r => r.UserId == user.Id).ExecuteDeleteAsync();
        await _context.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        await transaction.CommitAsync();
        SessionTokens.Forget(user.Id);
        return NoContent();
    }

    // The token was checked against the account on the way in (SessionTokens), so the user exists.
    private Task<User> CurrentUserAsync() => _context.Users.FirstAsync(u => u.Id == UserId);
}
