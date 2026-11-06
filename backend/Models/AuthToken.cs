namespace ExpenseTracker.Api.Models;

public static class AuthTokenPurpose
{
    public const string Reset = "reset";
    public const string Login = "login";
    public const string Verify = "verify";
}

// A single-use code sent by email (password reset, sign-in link, email verification). Only a SHA-256 hash of the
// code is stored, so a leaked database can't be used to take over accounts.
public class AuthToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Purpose { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    // The address the link was sent to. A link only counts for the account's current email.
    public string? Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
