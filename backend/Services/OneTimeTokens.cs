using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;

namespace ExpenseTracker.Api.Services;

// The codes inside emailed links. Each is 32 random bytes, works once, expires, and replaces any earlier code for
// the same purpose. Only its SHA-256 hash is stored.
public class OneTimeTokens
{
    // At most one email of each kind per account per minute, so nobody can flood an inbox (or the free email quota).
    public static readonly TimeSpan ResendWait = TimeSpan.FromMinutes(1);

    public static TimeSpan Lifetime(string purpose) => purpose switch
    {
        AuthTokenPurpose.Reset => TimeSpan.FromMinutes(30),
        AuthTokenPurpose.Login => TimeSpan.FromMinutes(15),
        AuthTokenPurpose.Verify => TimeSpan.FromHours(24),
        _ => throw new ArgumentOutOfRangeException(nameof(purpose))
    };

    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public OneTimeTokens(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    // A new code for the link sent to `email`, or null when one of this kind went out less than a minute ago.
    public async Task<string?> IssueAsync(User user, string purpose, string email)
    {
        var now = _clock.UtcNow;
        var lastSent = await _db.AuthTokens
            .Where(t => t.UserId == user.Id && t.Purpose == purpose)
            .MaxAsync(t => (DateTime?)t.CreatedAt);
        if (lastSent > now - ResendWait) return null;

        // Older codes of this kind stop working; expired ones of any kind are tidied away.
        await _db.AuthTokens
            .Where(t => t.UserId == user.Id && (t.Purpose == purpose || t.ExpiresAt <= now))
            .ExecuteDeleteAsync();

        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _db.AuthTokens.Add(new AuthToken
        {
            UserId = user.Id,
            Purpose = purpose,
            TokenHash = Hash(code),
            Email = email,
            CreatedAt = now,
            ExpiresAt = now + Lifetime(purpose)
        });
        await _db.SaveChangesAsync();
        return code;
    }

    // The unused, unexpired token behind a code (with its user), or null.
    public async Task<AuthToken?> FindAsync(string? code, string purpose)
    {
        code = code?.Trim();
        if (string.IsNullOrEmpty(code) || code.Length > 100) return null;
        var hash = Hash(code);
        var now = _clock.UtcNow;
        return await _db.AuthTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose && t.UsedAt == null && t.ExpiresAt > now);
    }

    // Marks the token used. Atomic, so two requests racing with the same link can't both succeed.
    public async Task<bool> UseAsync(AuthToken token)
    {
        var now = _clock.UtcNow;
        var claimed = await _db.AuthTokens
            .Where(t => t.Id == token.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now));
        return claimed == 1;
    }

    // Every code of these kinds is forgotten: unused links stop working (after a password or email change), and a new
    // link can be sent straight away.
    public Task RevokeAsync(int userId, params string[] purposes) =>
        _db.AuthTokens.Where(t => t.UserId == userId && purposes.Contains(t.Purpose)).ExecuteDeleteAsync();

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
