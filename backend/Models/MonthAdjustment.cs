namespace ExpenseTracker.Api.Models;

// Legacy (v1) one-off salary change per budget month. Read once by LegacyBackfillService, which turns it into
// a salary Adjustment component; nothing else uses it.
public class MonthAdjustment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal SalaryAdjust { get; set; }
    public string Note { get; set; } = string.Empty;
}
