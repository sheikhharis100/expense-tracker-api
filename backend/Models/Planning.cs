namespace ExpenseTracker.Api.Models;

// Budget target that applies from an effective month until replaced. Limit null means "no budget from here".
public class BudgetTemplate
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public int EffectiveYear { get; set; }
    public int EffectiveMonth { get; set; }
    public decimal? MonthlyLimit { get; set; }
}

// Opening balance (when carryover is off, or for the first month) and money set aside, per budget month.
public class MonthSetting
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal? OpeningBalance { get; set; }
    public decimal ReservedSavings { get; set; }
    public string Note { get; set; } = string.Empty;
    public uint Version { get; set; }
}

// One version of a repeating item. Editing "this and future" ends this version and starts a new one in the
// same series, so earlier months (and paid records) keep the old values.
public class RecurringItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid SeriesId { get; set; }
    // Expense (planned spending) or Income (expected other income).
    public string Kind { get; set; } = TxKind.Expense;
    public string Description { get; set; } = string.Empty;
    public int? AccountId { get; set; }
    public Account? Account { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public decimal Amount { get; set; }
    public int DayOfMonth { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public int StartYear { get; set; }
    public int StartMonth { get; set; }
    public int? EndYear { get; set; }
    public int? EndMonth { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public uint Version { get; set; }
}

// A planned instance of a recurring item in one month. Paying it links exactly one transaction (or receipt).
public class RecurringOccurrence
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RecurringItemId { get; set; }
    public RecurringItem RecurringItem { get; set; } = null!;
    public Guid SeriesId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = OccurrenceStatus.Planned;
    public bool IsOverridden { get; set; }
    public int? TransactionId { get; set; }
    public int? IncomeReceiptId { get; set; }
    public string Note { get; set; } = string.Empty;
    public uint Version { get; set; }
}

// A user-defined column of the expense ledger. Values live in Transaction.CustomValuesJson under Key.
public class CustomColumn
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string TableKey { get; set; } = "expenses";
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    // text, money, number, date, dropdown, checkbox, formula
    public string DataType { get; set; } = "text";
    public string OptionsJson { get; set; } = "[]";
    // Canonical formula with [#key] references (formula columns only).
    public string Formula { get; set; } = string.Empty;
    public int Decimals { get; set; } = 2;
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public uint Version { get; set; }
}

// Column order, widths and visibility for a table. A "template" row applies from its month onwards;
// a "month" row overrides one month only.
public class TableLayout
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string TableKey { get; set; } = "expenses";
    public string Scope { get; set; } = "template";
    public int Year { get; set; }
    public int Month { get; set; }
    public string ColumnsJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; }
}
