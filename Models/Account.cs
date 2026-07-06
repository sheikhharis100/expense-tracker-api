namespace ExpenseTracker.Api.Models;

public class Account
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public List<Transaction> Transactions { get; set; } = new();
}