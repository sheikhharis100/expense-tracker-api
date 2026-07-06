namespace ExpenseTracker.Api.Models;

public class BudgetAlert
{
    public int Id { get; set; }
    public int BudgetId { get; set; }
    public Budget Budget { get; set; } = null!;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}