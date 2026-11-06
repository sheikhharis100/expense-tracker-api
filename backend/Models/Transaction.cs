namespace ExpenseTracker.Api.Models;

// One row of the ledger. Expenses and refunds affect spending; transfers move money between the user's own
// accounts and never count as income or spending.
public class Transaction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    // A local calendar date (Asia/Karachi by default) stored at midnight.
    public DateTime TransactionDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string Kind { get; set; } = TxKind.Expense;
    public string Status { get; set; } = TxStatus.Paid;
    public bool IsAdditional { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;

    public int? RefundOfId { get; set; }
    public Transaction? RefundOf { get; set; }
    public int? TransferAccountId { get; set; }
    public Account? TransferAccount { get; set; }
    public int? RecurringOccurrenceId { get; set; }

    // Values of custom columns keyed by column key ("c12"). Display-only: never part of any total.
    public string CustomValuesJson { get; set; } = "{}";

    // Lets a client retry a create without creating a duplicate.
    public Guid? ClientRequestId { get; set; }

    // Postgres xmin; stale edits fail instead of overwriting newer changes.
    public uint Version { get; set; }
}
