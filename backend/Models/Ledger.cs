namespace ExpenseTracker.Api.Models;

// String values stored in the database; kept as constants so queries and DTOs agree.
public static class TxKind
{
    public const string Expense = "Expense";
    public const string Refund = "Refund";
    public const string Transfer = "Transfer";
    // Income rows from before salary/income receipts existed; kept for history, excluded from every total.
    public const string LegacyIncome = "LegacyIncome";

    public static readonly string[] Editable = { Expense, Refund, Transfer };
}

public static class TxStatus
{
    public const string Paid = "Paid";
    public const string Planned = "Planned";
}

public static class ReceiptKind
{
    public const string Salary = "Salary";
    public const string Other = "Other";
}

public static class ReceiptStatus
{
    public const string Received = "Received";
    public const string Expected = "Expected";
}

public static class SalaryComponentKind
{
    public const string Base = "Base";
    public const string Allowance = "Allowance";
    public const string Bonus = "Bonus";
    public const string Overtime = "Overtime";
    public const string Arrears = "Arrears";
    public const string Deduction = "Deduction";
    public const string Adjustment = "Adjustment";

    public static readonly string[] All = { Base, Allowance, Bonus, Overtime, Arrears, Deduction, Adjustment };
}

public static class OccurrenceStatus
{
    public const string Planned = "Planned";
    public const string Paid = "Paid";
    public const string Skipped = "Skipped";
}

public static class PaymentMethods
{
    public static readonly string[] All = { "Cash", "Debit card", "Credit card", "Bank transfer", "JazzCash", "Easypaisa", "Other" };
}
