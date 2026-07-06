using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Api.DTOs;

public class AccountCreateDto
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression("Checking|Savings|CreditCard")]
    public string Type { get; set; } = string.Empty;

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = string.Empty;

    public decimal OpeningBalance { get; set; }
}

public class AccountUpdateDto
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression("Checking|Savings|CreditCard")]
    public string Type { get; set; } = string.Empty;

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = string.Empty;

    public decimal OpeningBalance { get; set; }
}

public class AccountResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public List<TransactionResponseDto> Transactions { get; set; } = new();
}

public class CategoryCreateDto
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression("Income|Expense")]
    public string Type { get; set; } = string.Empty;
}

public class CategoryUpdateDto
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression("Income|Expense")]
    public string Type { get; set; } = string.Empty;
}

public class CategoryResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public class TransactionCreateDto
{
    [Range(1, int.MaxValue)]
    public int AccountId { get; set; }

    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
}

public class TransactionUpdateDto
{
    [Range(1, int.MaxValue)]
    public int AccountId { get; set; }

    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
}

public class TransactionResponseDto
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TransactionFilterDto
{
    public int? AccountId { get; set; }
    public int? CategoryId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class BudgetCreateDto
{
    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal MonthlyLimit { get; set; }

    [Range(1, 12)]
    public int Month { get; set; }

    [Range(2000, 3000)]
    public int Year { get; set; }
}

public class BudgetUpdateDto
{
    [Range(0.01, double.MaxValue)]
    public decimal MonthlyLimit { get; set; }
}

public class BudgetResponseDto
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal MonthlyLimit { get; set; }
    public decimal CurrentSpend { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal PercentUsed { get; set; }
    public bool IsOverBudget { get; set; }
}

public class CsvImportResponseDto
{
    public int TotalRows { get; set; }
    public int ImportedSuccessfully { get; set; }
    public int Skipped { get; set; }
    public List<CsvImportErrorDto> Errors { get; set; } = new();
}

public class CsvImportErrorDto
{
    public int Row { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class SpendingByCategoryDto
{
    public string Category { get; set; } = string.Empty;
    public decimal TotalSpent { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class TrendDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal TotalSpent { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class BudgetStatusDto
{
    public int BudgetId { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Limit { get; set; }
    public decimal Spent { get; set; }
    public decimal PercentUsed { get; set; }
    public bool IsOverThreshold { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class BudgetAlertResponseDto
{
    public int Id { get; set; }
    public int BudgetId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class RecurringTransactionCreateDto
{
    [Range(1, int.MaxValue)]
    public int AccountId { get; set; }

    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;

    [Range(1, 28)]
    public int DayOfMonth { get; set; }
}

public class RecurringTransactionUpdateDto
{
    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;

    [Range(1, 28)]
    public int DayOfMonth { get; set; }

    public bool IsActive { get; set; }
}

public class RecurringTransactionResponseDto
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public int DayOfMonth { get; set; }
    public DateTime NextRunDate { get; set; }
    public bool IsActive { get; set; }
}