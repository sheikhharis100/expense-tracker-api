namespace ExpenseTracker.Api.Models;

// Recurring salary plan: from this earned month on, the expected salary is Amount (base) + Allowances,
// until a later plan replaces it. A planning template only; it never creates received income.
public class SalaryPlan
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public decimal Allowances { get; set; }
    public string Note { get; set; } = string.Empty;
    public uint Version { get; set; }
}

// The salary earned for one month and the budget month it funds. Once saved it no longer follows the plan,
// so later plan changes never rewrite it.
public class SalaryStatement
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int EarnedYear { get; set; }
    public int EarnedMonth { get; set; }
    public int FundedYear { get; set; }
    public int FundedMonth { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<SalaryComponent> Components { get; set; } = new();
    public uint Version { get; set; }
}

// Base, Allowance, Bonus, Overtime, Arrears: added. Deduction: subtracted (stored positive). Adjustment: signed.
// Deductions only reduce expected net salary; they are never expenses.
public class SalaryComponent
{
    public int Id { get; set; }
    public int StatementId { get; set; }
    public SalaryStatement Statement { get; set; } = null!;
    public string Kind { get; set; } = SalaryComponentKind.Base;
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int SortOrder { get; set; }
}

// Money actually received (or expected, for other income). Only Received receipts count, and only through
// their allocations to budget months.
public class IncomeReceipt
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Kind { get; set; } = ReceiptKind.Other;
    public int? SalaryStatementId { get; set; }
    public SalaryStatement? SalaryStatement { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string Status { get; set; } = ReceiptStatus.Received;
    public DateOnly? ReceivedDate { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public int? RecurringOccurrenceId { get; set; }
    public Guid? ClientRequestId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<IncomeAllocation> Allocations { get; set; } = new();
    public uint Version { get; set; }
}

// Part of a receipt assigned to a budget month. Allocations of a receipt never exceed its amount.
public class IncomeAllocation
{
    public int Id { get; set; }
    public int ReceiptId { get; set; }
    public IncomeReceipt Receipt { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
}
