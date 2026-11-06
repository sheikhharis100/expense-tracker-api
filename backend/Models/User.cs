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
    public List<Account> Accounts { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
}
