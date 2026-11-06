using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Api.DTOs;

public class AccountCreateDto
{
    [Required(ErrorMessage = "Give the account a name."), StringLength(100, ErrorMessage = "Account names can be at most 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose an account type."),
     RegularExpression("Cash|Wallet|Checking|Savings|CreditCard", ErrorMessage = "Account type must be Cash, Wallet, Checking, Savings or CreditCard.")]
    public string Type { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a currency."), StringLength(3, MinimumLength = 3, ErrorMessage = "Currency must be a 3-letter code like PKR.")]
    public string Currency { get; set; } = string.Empty;

    public decimal OpeningBalance { get; set; }
}

public class AccountUpdateDto
{
    [Required(ErrorMessage = "Give the account a name."), StringLength(100, ErrorMessage = "Account names can be at most 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose an account type."),
     RegularExpression("Cash|Wallet|Checking|Savings|CreditCard", ErrorMessage = "Account type must be Cash, Wallet, Checking, Savings or CreditCard.")]
    public string Type { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a currency."), StringLength(3, MinimumLength = 3, ErrorMessage = "Currency must be a 3-letter code like PKR.")]
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
    public List<ExpenseRowDto> Transactions { get; set; } = new();
}

public class CategoryCreateDto
{
    [Required(ErrorMessage = "Give the category a name."), StringLength(100, ErrorMessage = "Category names can be at most 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose spending or income."),
     RegularExpression("Income|Expense", ErrorMessage = "Type must be Income or Expense.")]
    public string Type { get; set; } = string.Empty;

    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Colour must look like #2a78d6.")]
    public string? Color { get; set; }

    [StringLength(50, ErrorMessage = "Group names can be at most 50 characters.")]
    public string? GroupName { get; set; }
}

public class CategoryUpdateDto
{
    [Required(ErrorMessage = "Give the category a name."), StringLength(100, ErrorMessage = "Category names can be at most 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose spending or income."),
     RegularExpression("Income|Expense", ErrorMessage = "Type must be Income or Expense.")]
    public string Type { get; set; } = string.Empty;

    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Colour must look like #2a78d6.")]
    public string? Color { get; set; }

    [StringLength(50, ErrorMessage = "Group names can be at most 50 characters.")]
    public string? GroupName { get; set; }

    public bool IsArchived { get; set; }

    // Omitted: the edit overwrites whatever is saved.
    public uint? Version { get; set; }
}

public class CategoryReorderDto
{
    [Required(ErrorMessage = "List the categories in their new order."), MinLength(1, ErrorMessage = "List the categories in their new order.")]
    public List<int> OrderedIds { get; set; } = new();
}

public class CategoryResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string? GroupName { get; set; }
    public bool IsArchived { get; set; }
    public int TransactionCount { get; set; }
    public uint Version { get; set; }
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
