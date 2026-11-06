using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.DTOs;

// ---------- Profile ----------

public class MeResponseDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public int SalaryLagMonths { get; set; }
    public bool CarryoverEnabled { get; set; }
    public int? DefaultAccountId { get; set; }
    public DateOnly Today { get; set; }
    public YearMonth CurrentMonth { get; set; }
    public List<string> SupportedCurrencies { get; set; } = new();
    public List<string> PaymentMethods { get; set; } = new();
    public bool NeedsSetup { get; set; }
    public bool EmailVerified { get; set; }
    // The local date the account was created.
    public DateOnly MemberSince { get; set; }
}

public class ProfileStatsDto
{
    // Spending, refund and transfer rows plus income receipts.
    public int EntriesLogged { get; set; }
    // Calendar months from the first entry's month to this month, both included; 0 with no entries.
    public int MonthsTracked { get; set; }
    public YearMonth? FirstMonth { get; set; }
}

public class MeUpdateDto
{
    [StringLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "PKR";

    [Required, StringLength(64)]
    public string TimeZone { get; set; } = LocalCalendar.DefaultZone;

    [Range(0, 3)]
    public int SalaryLagMonths { get; set; } = 1;

    public bool CarryoverEnabled { get; set; }

    public int? DefaultAccountId { get; set; }
}

// ---------- Summary ----------

public class ComparisonDto
{
    public YearMonth Month { get; set; }
    public decimal ReceivedIncome { get; set; }
    public decimal NetSpending { get; set; }
    public decimal RemainingBalance { get; set; }
    public decimal NetSpendingChange { get; set; }
    public decimal? NetSpendingChangePercent { get; set; }
    public decimal ReceivedIncomeChange { get; set; }
}

public class MonthSummaryDto
{
    public string Currency { get; set; } = string.Empty;
    public DateOnly Today { get; set; }
    public string Basis { get; set; } = MonthFigures.Basis;
    public MonthFigures Figures { get; set; } = null!;
    public ComparisonDto Previous { get; set; } = null!;
    public CashFlowFigures CashFlow { get; set; } = null!;
}

public class AnnualRowDto
{
    public YearMonth Month { get; set; }
    public string Timing { get; set; } = string.Empty;
    public decimal ExpectedSalary { get; set; }
    public decimal SalaryIncome { get; set; }
    public decimal OtherIncome { get; set; }
    public decimal ReceivedIncome { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal GrossSpending { get; set; }
    public decimal Refunds { get; set; }
    public decimal NetSpending { get; set; }
    public decimal AdditionalNet { get; set; }
    public decimal Surplus { get; set; }
    public decimal RemainingBalance { get; set; }
    public decimal? IncomeUsedPercent { get; set; }
    public decimal? TotalBudget { get; set; }
    public decimal UnpaidCommitments { get; set; }
}

public class AnnualSummaryDto
{
    public int Year { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Basis { get; set; } = MonthFigures.Basis;
    public List<AnnualRowDto> Rows { get; set; } = new();
    public AnnualRowDto Totals { get; set; } = new();
    public List<CategoryTotalDto> Categories { get; set; } = new();
}

public class CategoryTotalDto
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public decimal Net { get; set; }
}

// ---------- Expenses ledger ----------

public class ExpenseInputDto
{
    public Guid? ClientRequestId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }

    [RegularExpression("Expense|Refund|Transfer")]
    public string Kind { get; set; } = "Expense";

    [RegularExpression("Paid|Planned")]
    public string Status { get; set; } = "Paid";

    public int? CategoryId { get; set; }

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [StringLength(40)]
    public string PaymentMethod { get; set; } = string.Empty;

    public bool IsAdditional { get; set; }
    public int? AccountId { get; set; }
    public int? TransferAccountId { get; set; }
    public int? RefundOfId { get; set; }
    public Dictionary<string, JsonElement>? Custom { get; set; }
    public uint? Version { get; set; }
}

// Inline edits: only the fields present are changed.
public class ExpensePatchDto
{
    public uint Version { get; set; }
    public DateOnly? Date { get; set; }
    public decimal? Amount { get; set; }

    [RegularExpression("Paid|Planned")]
    public string? Status { get; set; }

    public int? CategoryId { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [StringLength(40)]
    public string? PaymentMethod { get; set; }

    public bool? IsAdditional { get; set; }
    public Dictionary<string, JsonElement>? Custom { get; set; }
}

public class DuplicateDto
{
    public Guid? ClientRequestId { get; set; }
    public DateOnly? Date { get; set; }
}

public class RefundInputDto
{
    public Guid? ClientRequestId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;
}

public class CellValueDto
{
    public JsonElement? Value { get; set; }
    public string? Display { get; set; }
    public string? Error { get; set; }
}

public class ExpenseRowDto
{
    public int Id { get; set; }
    public uint Version { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryColor { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public bool IsAdditional { get; set; }
    public bool IsRecurring { get; set; }
    public int? RecurringOccurrenceId { get; set; }
    public int? RefundOfId { get; set; }
    public decimal RefundedAmount { get; set; }
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public int? TransferAccountId { get; set; }
    public string? TransferAccountName { get; set; }
    public Dictionary<string, JsonElement> Custom { get; set; } = new();
    public Dictionary<string, CellValueDto> Computed { get; set; } = new();
}

public class LedgerTotalsDto
{
    public int Count { get; set; }
    public decimal Gross { get; set; }
    public decimal Refunds { get; set; }
    public decimal Net { get; set; }
    public decimal Planned { get; set; }
    public decimal Transfers { get; set; }
    public decimal Additional { get; set; }
}

public class ExpenseListDto
{
    public YearMonth Month { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<ColumnDto> Columns { get; set; } = new();
    public LayoutDto Layout { get; set; } = new();
    public List<ExpenseRowDto> Rows { get; set; } = new();
    // Totals of the rows matching the current filters.
    public LedgerTotalsDto Filtered { get; set; } = new();
    // The whole month, from the calculation engine (unaffected by filters).
    public LedgerTotalsDto MonthTotals { get; set; } = new();
    public bool IsFiltered { get; set; }
}

public class ExpenseFilterDto
{
    public string? Month { get; set; }
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Status { get; set; }
    public string? Kind { get; set; }
    public bool? Additional { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Sort { get; set; }
    public string? Dir { get; set; }
}

// ---------- Daily matrix ----------

public class MatrixEntryDto
{
    public int Id { get; set; }
    public uint Version { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public bool IsAdditional { get; set; }
}

public class MatrixCellDto
{
    public decimal Net { get; set; }
    public decimal Planned { get; set; }
    public List<MatrixEntryDto> Entries { get; set; } = new();
}

public class MatrixDayDto
{
    public DateOnly Date { get; set; }
    public Dictionary<int, MatrixCellDto> Cells { get; set; } = new();
    public decimal Net { get; set; }
    public string Note { get; set; } = string.Empty;
}

public class MatrixCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string? GroupName { get; set; }
    public bool IsArchived { get; set; }
    public bool Hidden { get; set; }
    public int? Width { get; set; }
}

public class MatrixDto
{
    public YearMonth Month { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly Today { get; set; }
    public string LayoutSource { get; set; } = "default";
    public List<MatrixCategoryDto> Categories { get; set; } = new();
    public List<MatrixDayDto> Days { get; set; } = new();
    public Dictionary<int, decimal> ColumnTotals { get; set; } = new();
    public decimal Net { get; set; }
}

public class DayNoteUpdateDto
{
    public DateOnly Date { get; set; }

    [StringLength(1000)]
    public string Text { get; set; } = string.Empty;
}

// ---------- Custom columns & layouts ----------

public class ColumnDto
{
    public string Key { get; set; } = string.Empty;
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool BuiltIn { get; set; }
    // Essential fields can be hidden but never removed.
    public bool Essential { get; set; }
    public bool Editable { get; set; }
    public List<string> Options { get; set; } = new();
    public string? Formula { get; set; }
    public string? FormulaError { get; set; }
    public int Decimals { get; set; }
    public bool IsArchived { get; set; }
    public uint Version { get; set; }
}

public class ColumnInputDto
{
    [Required, StringLength(60, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression("text|money|number|date|dropdown|checkbox|formula")]
    public string Type { get; set; } = "text";

    [MaxLength(100)]
    public List<string> Options { get; set; } = new();

    [StringLength(1000)]
    public string? Formula { get; set; }

    [Range(0, 6)]
    public int Decimals { get; set; } = 2;

    public uint? Version { get; set; }
}

public class FormulaCheckDto
{
    [StringLength(1000)]
    public string Formula { get; set; } = string.Empty;
    public int? ColumnId { get; set; }
}

public class FormulaCheckResultDto
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? Canonical { get; set; }
    public string? Display { get; set; }
    public List<string> References { get; set; } = new();
}

public class ColumnRemovalImpactDto
{
    public string Error { get; set; } = string.Empty;
    public int ValueCount { get; set; }
    public List<string> DependentColumns { get; set; } = new();
}

public class LayoutColumnDto
{
    [Required, StringLength(40)]
    public string Key { get; set; } = string.Empty;

    [Range(40, 800)]
    public int? Width { get; set; }

    public bool Hidden { get; set; }
}

public class LayoutDto
{
    public string Table { get; set; } = "expenses";
    // month | template | default
    public string Source { get; set; } = "default";
    public YearMonth? SourceMonth { get; set; }
    public List<LayoutColumnDto> Columns { get; set; } = new();
}

public class LayoutUpdateDto
{
    // month: this month only; future: this month and every later month (replaces later month-only layouts).
    [Required, RegularExpression("month|future")]
    public string Scope { get; set; } = "month";

    [MaxLength(200)]
    public List<LayoutColumnDto> Columns { get; set; } = new();
}

// ---------- Budgets ----------

public class BudgetLineDto
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public decimal? Amount { get; set; }
    // month | template | none
    public string Source { get; set; } = "none";
    public decimal Net { get; set; }
    public decimal? Remaining { get; set; }
}

public class BudgetMonthDto
{
    public YearMonth Month { get; set; }
    public List<BudgetLineDto> Lines { get; set; } = new();
    public decimal? TotalBudget { get; set; }
    public decimal? OverallRemaining { get; set; }
    public decimal UnbudgetedSpending { get; set; }
    public List<string> UnbudgetedCategories { get; set; } = new();
    public bool CoverageWarning { get; set; }
}

public class BudgetSetDto
{
    public decimal? Amount { get; set; }

    [Required, RegularExpression("month|future")]
    public string Scope { get; set; } = "month";
}

// ---------- Recurring ----------

public class RecurringItemDto
{
    public int Id { get; set; }
    public Guid SeriesId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int DayOfMonth { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public YearMonth Start { get; set; }
    public YearMonth? End { get; set; }
    public bool IsActive { get; set; }
    public uint Version { get; set; }
}

public class RecurringInputDto
{
    [RegularExpression("Expense|Income")]
    public string Kind { get; set; } = "Expense";

    [Required, StringLength(200, MinimumLength = 1)]
    public string Description { get; set; } = string.Empty;

    public int? CategoryId { get; set; }
    public decimal Amount { get; set; }

    [Range(1, 31)]
    public int DayOfMonth { get; set; } = 1;

    [StringLength(40)]
    public string PaymentMethod { get; set; } = string.Empty;

    public YearMonth Start { get; set; }
    public YearMonth? End { get; set; }
    public uint? Version { get; set; }
}

public class OccurrenceDto
{
    public int Id { get; set; }
    public uint Version { get; set; }
    public int RecurringItemId { get; set; }
    public Guid SeriesId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsOverridden { get; set; }
    public int? TransactionId { get; set; }
    public int? IncomeReceiptId { get; set; }
    public string Note { get; set; } = string.Empty;
}

public class RecurringMonthDto
{
    public YearMonth Month { get; set; }
    public List<RecurringItemDto> Items { get; set; } = new();
    public List<OccurrenceDto> Occurrences { get; set; } = new();
    public decimal PlannedUnpaid { get; set; }
}

public class OccurrenceUpdateDto
{
    public uint Version { get; set; }
    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }

    [StringLength(500)]
    public string Note { get; set; } = string.Empty;
}

public class PayOccurrenceDto
{
    public Guid? ClientRequestId { get; set; }
    public DateOnly? Date { get; set; }
    public decimal? Amount { get; set; }
}

// ---------- Salary & income ----------

public class SalaryPlanDto
{
    public int Id { get; set; }
    public YearMonth EffectiveFrom { get; set; }
    public decimal BaseSalary { get; set; }
    public decimal Allowances { get; set; }
    public string Note { get; set; } = string.Empty;
    public uint Version { get; set; }
}

public class SalaryPlanInputDto
{
    public YearMonth EffectiveFrom { get; set; }
    public decimal BaseSalary { get; set; }
    public decimal Allowances { get; set; }

    [StringLength(200)]
    public string Note { get; set; } = string.Empty;

    // Omitted: the plan for that month is overwritten whatever its version.
    public uint? Version { get; set; }
}

public class SalaryComponentDto
{
    [Required, RegularExpression("Base|Allowance|Bonus|Overtime|Arrears|Deduction|Adjustment")]
    public string Kind { get; set; } = "Base";

    [StringLength(100)]
    public string Label { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}

public class SalaryStatementInputDto
{
    public YearMonth Funded { get; set; }

    [StringLength(500)]
    public string Note { get; set; } = string.Empty;

    [MaxLength(50)]
    public List<SalaryComponentDto> Components { get; set; } = new();

    public uint? Version { get; set; }
}

public class AllocationDto
{
    public YearMonth Month { get; set; }
    public decimal Amount { get; set; }
}

public class ReceiptDto
{
    public int Id { get; set; }
    public uint Version { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int? SalaryStatementId { get; set; }
    public YearMonth? SalaryEarned { get; set; }
    public int? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateOnly? ReceivedDate { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public decimal Amount { get; set; }
    public decimal Unallocated { get; set; }
    public string Description { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public List<AllocationDto> Allocations { get; set; } = new();
}

public class ReceiptInputDto
{
    public Guid? ClientRequestId { get; set; }

    [RegularExpression("Salary|Other")]
    public string Kind { get; set; } = "Other";

    public int? CategoryId { get; set; }

    [RegularExpression("Received|Expected")]
    public string Status { get; set; } = "Received";

    public DateOnly? ReceivedDate { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public decimal Amount { get; set; }

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [StringLength(40)]
    public string PaymentMethod { get; set; } = string.Empty;

    // Omitted: the whole amount goes to the default month (the salary's funded month, or the given month).
    [MaxLength(24)]
    public List<AllocationDto>? Allocations { get; set; }

    public YearMonth? DefaultMonth { get; set; }
    public uint? Version { get; set; }
}

public class SalaryStatementDto
{
    public int? Id { get; set; }
    public uint Version { get; set; }
    public YearMonth Earned { get; set; }
    public YearMonth Funded { get; set; }
    public bool IsPlanned { get; set; }
    public string Note { get; set; } = string.Empty;
    public List<SalaryComponentDto> Components { get; set; } = new();
    public decimal ExpectedNet { get; set; }
    public decimal Received { get; set; }
    public decimal Outstanding { get; set; }
    public decimal Overpaid { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<ReceiptDto> Payments { get; set; } = new();
}

public class IncomeMonthDto
{
    public YearMonth Month { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int SalaryLagMonths { get; set; }
    public List<SalaryStatementDto> Funding { get; set; } = new();
    // The salary earned in this month (it funds a later month).
    public SalaryStatementDto? EarnedThisMonth { get; set; }
    public List<ReceiptDto> OtherIncome { get; set; } = new();
    public decimal ExpectedSalary { get; set; }
    public decimal SalaryReceived { get; set; }
    public decimal SalaryDue { get; set; }
    public decimal SalaryOverpaid { get; set; }
    public decimal SalaryIncome { get; set; }
    public decimal OtherIncomeReceived { get; set; }
    public decimal ExpectedOtherIncome { get; set; }
    public decimal TotalReceivedIncome { get; set; }
}

// ---------- Month settings & templates ----------

public class MonthSettingDto
{
    public YearMonth Month { get; set; }
    public decimal? OpeningBalance { get; set; }
    public decimal ReservedSavings { get; set; }
    public string Note { get; set; } = string.Empty;
    public bool CarryoverEnabled { get; set; }
    public uint Version { get; set; }
}

public class MonthSettingInputDto
{
    public decimal? OpeningBalance { get; set; }
    public decimal ReservedSavings { get; set; }

    [StringLength(1000)]
    public string Note { get; set; } = string.Empty;

    public uint? Version { get; set; }
}

public class MonthCopyDto
{
    public YearMonth From { get; set; }
    public bool Layout { get; set; } = true;
    public bool Budgets { get; set; } = true;
    public bool Recurring { get; set; } = true;
}

public class MonthCopyResultDto
{
    public int LayoutsCopied { get; set; }
    public int BudgetsCopied { get; set; }
    public int OccurrencesPlanned { get; set; }
}

// ---------- Sheet import ----------

public class SheetImportResultDto
{
    public int RowsRead { get; set; }
    public int DaysImported { get; set; }
    public int EntriesCreated { get; set; }
    public int NotesImported { get; set; }
    public int SalariesImported { get; set; }
    public int CellsSkipped { get; set; }
    public List<string> CategoriesCreated { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class ConflictDto
{
    public string Error { get; set; } = "This was changed on another device. Your edit wasn't saved; the latest version is shown.";
    public object? Current { get; set; }
}
