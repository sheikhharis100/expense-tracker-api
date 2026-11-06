using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Models;

namespace ExpenseTracker.Api.Services;

public static class SessionClaims
{
    // The account's SessionVersion when the token was issued.
    public const string Version = "sv";
}

// Issues the sign-in tokens (JWTs) and checks they're still current.
public class SessionTokens
{
    private readonly IConfiguration _config;

    public SessionTokens(IConfiguration config) => _config = config;

    public AuthResponse Issue(User user) => new()
    {
        Token = CreateJwt(user),
        UserId = user.Id,
        Email = user.Email
    };

    private string CreateJwt(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(SessionClaims.Version, user.SessionVersion.ToString(), ClaimValueTypes.Integer32)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(int.Parse(_config["Jwt:ExpiryMinutes"]!)),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // The version is read on every request; remembering it for a minute saves that database round trip. Every change
    // made here calls Forget, so a sign-out takes effect at once; another server instance would see it within the minute.
    private static readonly ConcurrentDictionary<int, (int? Version, DateTime Until)> Versions = new();
    private static readonly TimeSpan RememberFor = TimeSpan.FromMinutes(1);

    public static void Forget(int userId) => Versions.TryRemove(userId, out _);

    // Runs for every authenticated request and live-hub connection. A token from before a password change or
    // "sign out of all devices" (older SessionVersion), or for a deleted account, is refused. Tokens issued before
    // session versions existed have no claim and count as version 0, so nobody is signed out by the upgrade.
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!int.TryParse(principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            context.Fail("Invalid token");
            return;
        }
        var claimed = int.TryParse(principal!.FindFirst(SessionClaims.Version)?.Value, out var v) ? v : 0;

        int? current;
        if (Versions.TryGetValue(userId, out var known) && known.Until > DateTime.UtcNow)
            current = known.Version;
        else
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            current = await db.Users.Where(u => u.Id == userId).Select(u => (int?)u.SessionVersion).FirstOrDefaultAsync();
            Versions[userId] = (current, DateTime.UtcNow + RememberFor);
        }
        if (current != claimed) context.Fail("This session has ended");
    }
}
