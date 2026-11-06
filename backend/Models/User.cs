namespace ExpenseTracker.Api.Models;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Currency { get; set; } = "PKR";
    // IANA zone used for "today" and month boundaries.
    public string TimeZone { get; set; } = "Asia/Karachi";
    // Months between the month a salary is earned and the budget month it funds (1: October pay funds November).
    public int SalaryLagMonths { get; set; } = 1;
    // When on, each month opens with the previous month's closing balance.
    public bool CarryoverEnabled { get; set; }
    public int? DefaultAccountId { get; set; }
    // Bumped by one-time data backfills (see LegacyBackfillService).
    public int DataVersion { get; set; }
    // Set once the user opens a link sent to this address; cleared when the email changes.
    public DateTime? EmailVerifiedAt { get; set; }
    // Copied into every token ("sv" claim). Bumping it signs out every device holding an older token.
    public int SessionVersion { get; set; }
    // Consecutive wrong passwords; reaching the limit locks sign-in by password until LockedUntil.
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public List<Account> Accounts { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
}
