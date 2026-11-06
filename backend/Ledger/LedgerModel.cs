using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Ledger;

// Plain inputs for the calculator. LedgerLoader builds them from the database; tests build them directly.

public sealed record LedgerCategory(int Id, string Name, string Type, string Color, string? Group, bool IsArchived, int SortOrder);

// Kind: Expense | Refund | Transfer. Status: Paid | Planned. Only Paid expenses and refunds count.
public sealed record LedgerTxn(
    int Id,
    DateOnly Date,
    decimal Amount,
    int? CategoryId,
    string Kind,
    string Status,
    bool IsAdditional,
    int? RefundOfId = null,
    bool RefundOfIsAdditional = false);

public sealed record LedgerAllocation(YearMonth Month, decimal Amount);

// Kind: Salary | Other. Status: Received | Expected. Only Received receipts count, through their allocations.
public sealed record LedgerReceipt(
    int Id,
    string Kind,
    string Status,
    DateOnly? ReceivedDate,
    decimal Amount,
    int? StatementId,
    int? CategoryId,
    IReadOnlyList<LedgerAllocation> Allocations);

public sealed record LedgerComponent(string Kind, string Label, decimal Amount);

// IsPlanned: not saved yet, derived from the salary plan (expected only).
public sealed record LedgerStatement(int? Id, YearMonth Earned, YearMonth Funded, IReadOnlyList<LedgerComponent> Components, bool IsPlanned);

// Unpaid planned spending: a recurring occurrence still due, or a manually planned expense.
public sealed record LedgerCommitment(string Source, int Id, DateOnly DueDate, decimal Amount, int? CategoryId, string Name);

public sealed record LedgerMonthSetting(YearMonth Month, decimal? OpeningBalance, decimal ReservedSavings);

public sealed class LedgerData
{
    public required DateOnly Today { get; init; }
    public bool Carryover { get; init; }
    public IReadOnlyList<LedgerCategory> Categories { get; init; } = Array.Empty<LedgerCategory>();
    public IReadOnlyList<LedgerTxn> Transactions { get; init; } = Array.Empty<LedgerTxn>();
    public IReadOnlyList<LedgerReceipt> Receipts { get; init; } = Array.Empty<LedgerReceipt>();
    public IReadOnlyList<LedgerStatement> Statements { get; init; } = Array.Empty<LedgerStatement>();
    // Resolved budget per (category, month); missing key means no budget set.
    public IReadOnlyDictionary<(int CategoryId, YearMonth Month), decimal> Budgets { get; init; } =
        new Dictionary<(int, YearMonth), decimal>();
    public IReadOnlyList<LedgerCommitment> Commitments { get; init; } = Array.Empty<LedgerCommitment>();
    public IReadOnlyDictionary<YearMonth, LedgerMonthSetting> Settings { get; init; } =
        new Dictionary<YearMonth, LedgerMonthSetting>();
    // Earliest month with any money in it; the carryover chain starts here.
    public YearMonth? ChainStart { get; init; }
}
