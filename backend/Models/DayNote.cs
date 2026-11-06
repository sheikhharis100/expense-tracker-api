namespace ExpenseTracker.Api.Models;

// The "Remarks" column of the daily log.
public class DayNote
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateOnly Date { get; set; }
    public string Text { get; set; } = string.Empty;
}
