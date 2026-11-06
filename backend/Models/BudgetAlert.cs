namespace ExpenseTracker.Api.Models;

public class BudgetAlert
{
    public int Id { get; set; }
    // Older alerts point at an explicit Budget row; newer ones at the category and month.
    public int? BudgetId { get; set; }
    public Budget? Budget { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}
