using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;

namespace ExpenseTracker.Api.Services;

public class CsvImportService
{
    private readonly AppDbContext _context;
    private readonly AnalyticsService _analyticsService;

    public CsvImportService(AppDbContext context, AnalyticsService analyticsService)
    {
        _context = context;
        _analyticsService = analyticsService;
    }

    public async Task<ImportResult> ImportTransactionsAsync(int userId, Stream fileStream)
    {
        var result = new ImportResult();
        var accounts = await _context.Accounts.Where(a => a.UserId == userId).ToListAsync();
        var categories = await _context.Categories.Where(c => c.UserId == userId).ToListAsync();
        var affectedBudgetKeys = new HashSet<(int CategoryId, int Month, int Year)>();

        using var reader = new StreamReader(fileStream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        });

        if (!await csv.ReadAsync())
        {
            return result;
        }
        csv.ReadHeader();

        var rowNumber = 0;
        while (await csv.ReadAsync())
        {
            rowNumber++;
            result.TotalRows++;

            TransactionImportRow? row;
            try
            {
                row = csv.GetRecord<TransactionImportRow>();
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Errors.Add(new ImportError { Row = rowNumber, Reason = $"Could not parse row: {ex.Message}" });
                continue;
            }

            if (row == null)
            {
                result.Skipped++;
                result.Errors.Add(new ImportError { Row = rowNumber, Reason = "Could not parse row: empty or invalid record" });
                continue;
            }

            var account = accounts.FirstOrDefault(a => a.Name.Equals(row.Account, StringComparison.OrdinalIgnoreCase));
            if (account == null)
            {
                result.Skipped++;
                result.Errors.Add(new ImportError { Row = rowNumber, Reason = $"Unknown account '{row.Account}'" });
                continue;
            }

            var category = categories.FirstOrDefault(c => c.Name.Equals(row.Category, StringComparison.OrdinalIgnoreCase));
            if (category == null)
            {
                result.Skipped++;
                result.Errors.Add(new ImportError { Row = rowNumber, Reason = $"Unknown category '{row.Category}'" });
                continue;
            }

            if (!Money.IsValidAmount(row.Amount))
            {
                result.Skipped++;
                result.Errors.Add(new ImportError { Row = rowNumber, Reason = "Amount must be greater than zero with at most 2 decimals" });
                continue;
            }

            var date = DateOnly.FromDateTime(row.Date);
            if (category.Type == "Income")
            {
                // Income rows become received income for their month, not ledger entries.
                _context.IncomeReceipts.Add(new IncomeReceipt
                {
                    UserId = userId,
                    Kind = ReceiptKind.Other,
                    CategoryId = category.Id,
                    Status = ReceiptStatus.Received,
                    ReceivedDate = date,
                    Amount = row.Amount,
                    Description = row.Description,
                    CreatedAt = DateTime.UtcNow,
                    Allocations = { new IncomeAllocation { Year = date.Year, Month = date.Month, Amount = row.Amount } }
                });
                result.ImportedSuccessfully++;
                continue;
            }

            _context.Transactions.Add(new Transaction
            {
                UserId = userId,
                AccountId = account.Id,
                CategoryId = category.Id,
                Amount = row.Amount,
                Description = row.Description,
                TransactionDate = date.ToDateTime(TimeOnly.MinValue),
                Kind = TxKind.Expense,
                Status = TxStatus.Paid,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            result.ImportedSuccessfully++;
            affectedBudgetKeys.Add((category.Id, row.Date.Month, row.Date.Year));
        }

        await _context.SaveChangesAsync();

        foreach (var key in affectedBudgetKeys)
        {
            await _analyticsService.CheckBudgetAlertAsync(userId, key.CategoryId, key.Month, key.Year);
        }

        return result;
    }
}

public class TransactionImportRow
{
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
}

public class ImportResult
{
    public int TotalRows { get; set; }
    public int ImportedSuccessfully { get; set; }
    public int Skipped { get; set; }
    public List<ImportError> Errors { get; set; } = new();
}

public class ImportError
{
    public int Row { get; set; }
    public string Reason { get; set; } = string.Empty;
}
