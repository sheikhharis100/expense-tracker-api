namespace ExpenseTracker.Api.Models;

public class Category
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Color { get; set; } = "#2a78d6";
    public int SortOrder { get; set; }
    // Categories sharing a group get a combined total (e.g. "Travel" = InDrive + Bus).
    public string? GroupName { get; set; }
    // Archived categories disappear from pickers but keep their history and totals.
    public bool IsArchived { get; set; }
    public List<Transaction> Transactions { get; set; } = new();
    public List<Budget> Budgets { get; set; } = new();
}
