using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using CsvHelper.TypeConversion;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;

namespace ExpenseTracker.Api.Services;

public class CsvImportService
{
    private static readonly string[] RequiredHeaders = { "Date", "Amount", "Category", "Account" };
    private const int MaxReportedErrors = 100;

    private readonly AppDbContext _context;
    private readonly AnalyticsService _analyticsService;
    private readonly IClock _clock;

    public CsvImportService(AppDbContext context, AnalyticsService analyticsService, IClock clock)
    {
        _context = context;
        _analyticsService = analyticsService;
        _clock = clock;
    }

    public async Task<ImportResult> ImportTransactionsAsync(int userId, Stream fileStream)
    {
        var result = new ImportResult();
        var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        var today = LocalCalendar.Today(_clock, user.TimeZone);
        var accounts = await _context.Accounts.Where(a => a.UserId == userId).ToListAsync();
        var categories = await _context.Categories.Where(c => c.UserId == userId).ToListAsync();
        var affectedBudgetKeys = new HashSet<(int CategoryId, int Month, int Year)>();

        using var reader = new StreamReader(fileStream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            // Required columns are checked once below, instead of failing every row.
            HeaderValidated = null,
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant()
        });

        if (!await csv.ReadAsync())
        {
            result.Error = "The file is empty.";
            return result;
        }
        csv.ReadHeader();
        var headers = (csv.HeaderRecord ?? Array.Empty<string>()).Select(h => h.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = RequiredHeaders.Where(h => !headers.Contains(h)).ToList();
        if (missing.Count > 0)
        {
            result.Error = $"The file needs a header row with Date, Description, Amount, Category and Account columns (missing: {string.Join(", ", missing)}).";
            return result;
        }

        var rowNumber = 0;
        void Skip(string reason)
        {
            result.Skipped++;
            // Every skipped row is counted; the first hundred are explained.
            if (result.Errors.Count < MaxReportedErrors) result.Errors.Add(new ImportError { Row = rowNumber, Reason = reason });
        }

        while (await csv.ReadAsync())
        {
            rowNumber++;
            result.TotalRows++;

            TransactionImportRow? row;
            try
            {
                row = csv.GetRecord<TransactionImportRow>();
            }
            catch (TypeConverterException ex)
            {
                Skip($"{ex.MemberMapData?.Member?.Name ?? "A value"} “{ex.Text}” isn't valid");
                continue;
            }
            catch (CsvHelperException)
            {
                Skip("Couldn't read this row");
                continue;
            }

            if (row == null)
            {
                Skip("Could not parse row: empty or invalid record");
                continue;
            }

            var account = accounts.FirstOrDefault(a => a.Name.Equals(row.Account?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (account == null)
            {
                Skip($"Unknown account '{row.Account}'");
                continue;
            }

            var category = categories.FirstOrDefault(c => c.Name.Equals(row.Category?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (category == null)
            {
                Skip($"Unknown category '{row.Category}'");
                continue;
            }
            if (category.IsArchived)
            {
                Skip($"“{category.Name}” is archived; restore it to import entries");
                continue;
            }

            var date = DateOnly.FromDateTime(row.Date);
            var description = InputRules.Clean(row.Description);
            var problem = InputRules.Amount(row.Amount) ?? InputRules.Date(date)
                ?? InputRules.Text(description, InputRules.DescriptionLength, "Description");
            if (problem == null && date > today)
                problem = $"{date:dd MMM yyyy} is after today; only money already spent or received can be imported";
            if (problem != null)
            {
                Skip(problem.TrimEnd('.'));
                continue;
            }

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
                    Description = description,
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
                Description = description,
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
    // Day first, as dates are written in Pakistan (05/11/2026 is 5 November); ISO and written-out months too.
    [Format("yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy",
        "d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy", "d-MMM-yyyy", "dd-MMM-yyyy", "yyyy-MM-ddTHH:mm:ss", "d/M/yyyy H:mm", "d/M/yyyy H:mm:ss")]
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
    // The whole file was unusable (empty, wrong columns); nothing was imported.
    public string? Error { get; set; }
}

public class ImportError
{
    public int Row { get; set; }
    public string Reason { get; set; } = string.Empty;
}
