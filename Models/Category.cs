namespace ExpenseTracker.Api.Models;

public class Category
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<Transaction> Transactions { get; set; } = new();
    public List<Budget> Budgets { get; set; } = new();
}