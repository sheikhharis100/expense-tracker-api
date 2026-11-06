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

// Everything that works while signed out: sign-up, sign-in, and the emailed links.
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    // Same answer whether or not the address has an account, so nobody can find out who uses the app.
    public const string ResetSentMessage = "If that email has an account, a reset link is on its way. Check spam too.";
    public const string LinkSentMessage = "If that email has an account, a sign-in link is on its way. Check spam too.";
    public const string LinkExpired = "This link has expired or was already used. Ask for a new one.";

    // Checked when the email isn't registered, so a wrong address takes as long as a wrong password.
    private static readonly Lazy<string> DummyHash = new(() => BCrypt.Net.BCrypt.HashPassword("no-such-account"));

    private readonly AppDbContext _context;
    private readonly IClock _clock;
    private readonly PasswordPolicy _passwords;
    private readonly SessionTokens _sessions;
    private readonly OneTimeTokens _tokens;
    private readonly AccountEmails _emails;
    private readonly LedgerNotifier _notifier;

    public AuthController(AppDbContext context, IClock clock, PasswordPolicy passwords, SessionTokens sessions,
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

    [HttpGet("options")]
    public ActionResult<AuthOptionsResponse> Options() => Ok(new AuthOptionsResponse { EmailEnabled = _emails.Enabled });

    [HttpPost("register")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var email = AccountRules.NormalizeEmail(request.Email);
        var name = AccountRules.NormalizeName(request.DisplayName);
        var problem = AccountRules.CheckEmail(email) ?? AccountRules.CheckName(name)
            ?? await _passwords.CheckAsync(request.Password, email, ct);
        if (problem != null) return Error(problem);
        if (await FindByEmailAsync(email) != null) return Conflict(new { error = AccountRules.EmailTaken });

        var user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CreatedAt = _clock.UtcNow,
            DisplayName = name,
            // New accounts start on the current data model; nothing to backfill.
            DataVersion = LegacyBackfillService.CurrentVersion
        };

        _context.Users.Add(user);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two sign-ups with the same address at once; the unique index let one through.
            return Conflict(new { error = AccountRules.EmailTaken });
        }

        // Same columns as the sheet: Bill / Rent, Food, Travel (InDrive), Travel (Bus), Other Expenses, plus income lines.
        await DefaultData.SeedAsync(_context, user);

        // The account works straight away; the profile shows the address as unconfirmed until the link is opened.
        await _emails.SendVerificationAsync(user);

        return Ok(_sessions.Issue(user));
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await FindByEmailAsync(AccountRules.NormalizeEmail(request.Email));
        if (user == null)
        {
            BCrypt.Net.BCrypt.Verify(request.Password, DummyHash.Value);
            return Unauthorized(new { error = AccountRules.InvalidCredentials });
        }

        var now = _clock.UtcNow;
        // While locked even the right password is refused; otherwise guessing could simply carry on.
        if (user.LockedUntil > now) return LockedOut();

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            var locked = user.FailedLoginCount >= AccountRules.MaxFailedLogins;
            if (locked)
            {
                user.FailedLoginCount = 0;
                user.LockedUntil = now + AccountRules.LockoutDuration;
            }
            await _context.SaveChangesAsync();
            return locked ? LockedOut() : Unauthorized(new { error = AccountRules.InvalidCredentials });
        }

        if (user.FailedLoginCount != 0 || user.LockedUntil != null)
        {
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
            await _context.SaveChangesAsync();
        }
        return Ok(_sessions.Issue(user));
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult> ForgotPassword(EmailRequest request)
    {
        if (!_emails.Enabled) return EmailOff();
        var email = AccountRules.NormalizeEmail(request.Email);
        if (AccountRules.CheckEmail(email) is string problem) return Error(problem);

        // Only ever sent to the address on the account, never one taken from the request.
        var user = await FindByEmailAsync(email);
        if (user != null) await _emails.SendPasswordResetAsync(user);
        return Ok(new { message = ResetSentMessage });
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<AuthResponse>> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var token = await _tokens.FindAsync(request.Token, AuthTokenPurpose.Reset);
        if (token == null || token.Email != token.User.Email) return Error(LinkExpired);
        var user = token.User;

        // Checked before the link is used up, so a rejected password doesn't cost the person their link.
        if (await _passwords.CheckAsync(request.NewPassword, user.Email, ct) is string problem) return Error(problem);
        if (!await _tokens.UseAsync(token)) return Error(LinkExpired);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.SessionVersion++;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        // Opening the link proved they read this inbox.
        user.EmailVerifiedAt ??= _clock.UtcNow;
        await _context.SaveChangesAsync();
        SessionTokens.Forget(user.Id);
        await _tokens.RevokeAsync(user.Id, AuthTokenPurpose.Reset, AuthTokenPurpose.Login);

        _emails.SendPasswordChangedNotice(user);
        // Other devices refresh, find their token refused and go to the sign-in screen.
        await _notifier.NotifyAsync(user.Id, LedgerScopes.Session);
        return Ok(_sessions.Issue(user));
    }

    [HttpPost("email-link")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult> EmailLink(EmailRequest request)
    {
        if (!_emails.Enabled) return EmailOff();
        var email = AccountRules.NormalizeEmail(request.Email);
        if (AccountRules.CheckEmail(email) is string problem) return Error(problem);

        var user = await FindByEmailAsync(email);
        if (user != null) await _emails.SendSignInLinkAsync(user);
        return Ok(new { message = LinkSentMessage });
    }

    [HttpPost("redeem-link")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<AuthResponse>> RedeemLink(TokenRequest request)
    {
        var token = await _tokens.FindAsync(request.Token, AuthTokenPurpose.Login);
        if (token == null || token.Email != token.User.Email || !await _tokens.UseAsync(token)) return Error(LinkExpired);

        var user = token.User;
        user.EmailVerifiedAt ??= _clock.UtcNow;
        // Like a reset, the link proves who they are, so a lockout from wrong passwords ends.
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        await _context.SaveChangesAsync();
        return Ok(_sessions.Issue(user));
    }

    [HttpPost("verify-email")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult> VerifyEmail(TokenRequest request)
    {
        var token = await _tokens.FindAsync(request.Token, AuthTokenPurpose.Verify);
        if (token == null) return Error("This link has expired or was already used. You can send a new one from your Profile.");
        var user = token.User;
        if (token.Email != user.Email) return Error("This link was for an email address the account no longer uses.");
        if (!await _tokens.UseAsync(token)) return Error(LinkExpired);

        user.EmailVerifiedAt ??= _clock.UtcNow;
        await _context.SaveChangesAsync();
        await _notifier.NotifyAsync(user.Id, LedgerScopes.Profile);
        return Ok(new { email = user.Email, message = "Your email is confirmed." });
    }

    private Task<User?> FindByEmailAsync(string email) => _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);

    private ActionResult Error(string message) => BadRequest(new { error = message });

    private ObjectResult LockedOut() => StatusCode(StatusCodes.Status429TooManyRequests, new { error = AccountRules.LockedOut });

    private ObjectResult EmailOff() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "Email isn't set up on this server yet, so links can't be sent." });
}
