using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

// Applies a parsed Daily Log export. Every filled cell replaces that day's entries for the column, so
// importing the same file twice gives the same result. Empty cells leave existing entries alone.
public class SheetImportService
{
    private readonly AppDbContext _context;
    private readonly IClock _clock;

    public SheetImportService(AppDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    private const int MaxWarnings = 50;

    private static void Warn(SheetImportResultDto result, string warning)
    {
        if (result.Warnings.Count < MaxWarnings) result.Warnings.Add(warning);
    }

    private static string Truncate(string text, int max) => text.Length > max ? text[..max].TrimEnd() : text;

    public async Task<SheetImportResultDto> ImportAsync(int userId, Stream stream)
    {
        using var reader = new StreamReader(stream);
        var parsed = SheetCsvParser.Parse(reader);

        var result = new SheetImportResultDto
        {
            RowsRead = parsed.RowsRead,
            CellsSkipped = parsed.CellsSkipped,
            Warnings = parsed.Warnings.Take(MaxWarnings).ToList()
        };

        // Only money already spent is imported: days out of range are dropped, and future days keep just their remarks.
        var user = await _context.Users.FirstAsync(u => u.Id == userId);
        var today = LocalCalendar.Today(_clock, user.TimeZone);
        var days = new List<SheetCsvParser.ParsedDay>();
        foreach (var day in parsed.Days)
        {
            if (!InputRules.IsInRange(day.Date))
            {
                result.CellsSkipped += day.Cells.Count;
                Warn(result, $"{day.Date:dd MMM yyyy}: dates must be between {InputRules.MinYear} and {InputRules.MaxYear}");
                continue;
            }
            if (day.Date > today && day.Cells.Count > 0)
            {
                result.CellsSkipped += day.Cells.Count;
                Warn(result, $"{day.Date:dd MMM yyyy} is after today; its amounts were skipped (plan future spending in the app)");
                if (day.Note.Length > 0) days.Add(day with { Cells = new Dictionary<string, List<decimal>>() });
                continue;
            }
            days.Add(day);
        }
        var salaries = parsed.Salaries.Where(s => YearMonth.IsValid(s.PayMonth.Year, s.PayMonth.Month)).ToList();

        if (days.Count == 0 && salaries.Count == 0 && parsed.Incomes.Count == 0)
            return result;

        var account = await DefaultData.GetOrCreateDefaultAccountAsync(_context, user);
        var categories = await _context.Categories.Where(c => c.UserId == userId).ToListAsync();

        var byColumn = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in parsed.CategoryColumns.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Exact name first, then the old/new spelling of the same column ("Travel (InDrive)" = "Travel — InDrive").
            var name = Truncate(InputRules.Clean(column), InputRules.NameLength);
            var category = categories.FirstOrDefault(c => c.Type == "Expense" && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? categories.FirstOrDefault(c => c.Type == "Expense" && DefaultData.SameCategory(c.Name, name));
            if (category == null)
            {
                category = new Category
                {
                    UserId = userId,
                    Name = name,
                    Type = "Expense",
                    Color = DefaultData.NextColor(categories.Select(c => c.Color)),
                    SortOrder = categories.Where(c => c.Type == "Expense").Select(c => c.SortOrder).DefaultIfEmpty(-1).Max() + 1
                };
                _context.Categories.Add(category);
                categories.Add(category);
                result.CategoriesCreated.Add(category.Name);
            }
            byColumn[column] = category;
        }

        var existingIds = byColumn.Values.Where(c => c.Id != 0).Select(c => c.Id).ToList();
        var fromDate = days.Count > 0 ? days.Min(d => d.Date) : DateOnly.MinValue;
        var toDate = (days.Count > 0 ? days.Max(d => d.Date) : DateOnly.MinValue).AddDays(1);
        var from = fromDate.ToDateTime(TimeOnly.MinValue);
        var to = toDate.ToDateTime(TimeOnly.MinValue);

        var existing = (await _context.Transactions
                .Where(t => t.UserId == userId && t.Kind == TxKind.Expense && t.CategoryId != null && existingIds.Contains(t.CategoryId.Value)
                    && t.TransactionDate >= from && t.TransactionDate < to)
                .OrderBy(t => t.Id)
                .ToListAsync())
            .ToLookup(t => (DateOnly.FromDateTime(t.TransactionDate), t.CategoryId!.Value));

        var notes = await _context.DayNotes
            .Where(n => n.UserId == userId && n.Date >= fromDate && n.Date < toDate)
            .ToDictionaryAsync(n => n.Date);

        foreach (var day in days)
        {
            var touched = false;
            foreach (var (column, amounts) in day.Cells)
            {
                var category = byColumn[column];
                if (category.IsArchived)
                {
                    result.CellsSkipped++;
                    Warn(result, $"{day.Date:dd MMM yyyy} · {category.Name}: the category is archived; restore it to import its amounts");
                    continue;
                }
                var current = category.Id == 0 ? new List<Transaction>() : existing[(day.Date, category.Id)].ToList();

                for (var i = 0; i < Math.Max(current.Count, amounts.Count); i++)
                {
                    if (i < current.Count && i < amounts.Count)
                        current[i].Amount = amounts[i];
                    else if (i < amounts.Count)
                        _context.Transactions.Add(new Transaction
                        {
                            UserId = userId,
                            Kind = TxKind.Expense,
                            Status = TxStatus.Paid,
                            UpdatedAt = DateTime.UtcNow,
                            AccountId = account.Id,
                            Category = category,
                            Amount = amounts[i],
                            Description = string.Empty,
                            TransactionDate = day.Date.ToDateTime(TimeOnly.MinValue),
                            CreatedAt = DateTime.UtcNow
                        });
                    else
                        _context.Transactions.Remove(current[i]);
                }

                result.EntriesCreated += amounts.Count;
                touched = true;
            }

            var text = Truncate(InputRules.Clean(day.Note), InputRules.NoteLength);
            if (text.Length > 0)
            {
                if (notes.TryGetValue(day.Date, out var note))
                    note.Text = text;
                else
                    _context.DayNotes.Add(new DayNote { UserId = userId, Date = day.Date, Text = text });
                result.NotesImported++;
                touched = true;
            }

            if (touched)
                result.DaysImported++;
        }

        // Salary lines ("Salary (Oct pay): 55,000") become the salary plan, plus a received payment for months
        // up to today (the sheet records money that was already spent). Existing payments are never duplicated.
        var plans = await _context.SalaryPlans.Where(s => s.UserId == userId).ToListAsync();
        foreach (var hint in salaries.OrderBy(s => s.PayMonth.Index))
        {
            var plannedNow = LedgerLoader.PlannedStatement(plans, hint.PayMonth, hint.PayMonth);
            if (plannedNow == null || SalaryMath.ExpectedNet(plannedNow.Components) != hint.Amount)
            {
                var plan = plans.FirstOrDefault(p => p.Year == hint.PayMonth.Year && p.Month == hint.PayMonth.Month);
                if (plan == null)
                {
                    plan = new SalaryPlan { UserId = userId, Year = hint.PayMonth.Year, Month = hint.PayMonth.Month, Note = "Imported from sheet" };
                    _context.SalaryPlans.Add(plan);
                    plans.Add(plan);
                }
                plan.Amount = hint.Amount;
                plan.Allowances = 0;
                result.SalariesImported++;
            }

            var funded = hint.PayMonth.AddMonths(user.SalaryLagMonths);
            if (funded > YearMonth.Of(today)) continue;
            var statement = await _context.SalaryStatements.Include(s => s.Components)
                .FirstOrDefaultAsync(s => s.UserId == userId && s.EarnedYear == hint.PayMonth.Year && s.EarnedMonth == hint.PayMonth.Month);
            if (statement != null && await _context.IncomeReceipts.AnyAsync(r => r.SalaryStatementId == statement.Id)) continue;
            // A salary saved in the app but never paid follows the sheet, like the plan does; otherwise the difference
            // would show as "salary still due" next to the payment recorded below.
            if (statement != null)
            {
                var expected = SalaryMath.ExpectedNet(statement.Components.Select(c => new LedgerComponent(c.Kind, c.Label, c.Amount)));
                if (expected != hint.Amount)
                {
                    _context.SalaryComponents.RemoveRange(statement.Components);
                    statement.Components.Clear();
                    statement.Components.Add(new SalaryComponent { Kind = SalaryComponentKind.Base, Label = "Base salary", Amount = hint.Amount });
                }
            }
            statement ??= new SalaryStatement
            {
                UserId = userId,
                EarnedYear = hint.PayMonth.Year,
                EarnedMonth = hint.PayMonth.Month,
                FundedYear = funded.Year,
                FundedMonth = funded.Month,
                Note = "Imported from sheet",
                CreatedAt = DateTime.UtcNow,
                Components = { new SalaryComponent { Kind = SalaryComponentKind.Base, Label = "Base salary", Amount = hint.Amount } }
            };
            if (statement.Id == 0) _context.SalaryStatements.Add(statement);
            var paidOn = DateOnly.FromDateTime(new YearMonth(statement.FundedYear, statement.FundedMonth).FirstDay);
            _context.IncomeReceipts.Add(new IncomeReceipt
            {
                UserId = userId,
                Kind = ReceiptKind.Salary,
                SalaryStatement = statement,
                Status = ReceiptStatus.Received,
                ReceivedDate = paidOn > today ? today : paidOn,
                Amount = hint.Amount,
                Description = "Imported from sheet",
                CreatedAt = DateTime.UtcNow,
                Allocations = { new IncomeAllocation { Year = statement.FundedYear, Month = statement.FundedMonth, Amount = hint.Amount } }
            });
        }

        // A month row's "Income: 60,500" beyond its salary (pending/extra and other income on the sheet's Summary) becomes
        // one received other-income entry for that month. Its request id is derived from the month, so importing again
        // updates it instead of adding another, and it goes away if the sheet no longer shows anything beyond salary.
        if (parsed.Incomes.Count > 0)
        {
            var otherIncome = categories.FirstOrDefault(c => c.Type == "Income" && !c.IsArchived && c.Name.Equals("Other income", StringComparison.OrdinalIgnoreCase));
            foreach (var hint in parsed.Incomes)
            {
                if (!YearMonth.IsValid(hint.Month.Year, hint.Month.Month) || hint.Month > YearMonth.Of(today)) continue;
                var salary = salaries.Where(s => s.PayMonth.AddMonths(user.SalaryLagMonths) == hint.Month).Sum(s => s.Amount);
                var extra = hint.Total - salary;
                var requestId = SheetIncomeId(userId, hint.Month);
                var imported = await _context.IncomeReceipts.Include(r => r.Allocations)
                    .FirstOrDefaultAsync(r => r.UserId == userId && r.ClientRequestId == requestId);
                if (extra <= 0)
                {
                    if (imported != null) _context.IncomeReceipts.Remove(imported);
                    continue;
                }

                var receivedOn = DateOnly.FromDateTime(hint.Month.FirstDay);
                if (receivedOn > today) receivedOn = today;
                if (imported == null)
                {
                    imported = new IncomeReceipt { UserId = userId, ClientRequestId = requestId, Kind = ReceiptKind.Other, CreatedAt = DateTime.UtcNow };
                    _context.IncomeReceipts.Add(imported);
                }
                imported.CategoryId = otherIncome?.Id;
                imported.Status = ReceiptStatus.Received;
                imported.ReceivedDate = receivedOn;
                imported.Amount = extra;
                imported.Description = "Income beyond salary (from the sheet)";
                imported.Allocations.Clear();
                imported.Allocations.Add(new IncomeAllocation { Year = hint.Month.Year, Month = hint.Month.Month, Amount = extra });
                result.OtherIncomeImported++;
            }
        }

        await _context.SaveChangesAsync();
        return result;
    }

    private static Guid SheetIncomeId(int userId, YearMonth month) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"sheet-other-income:{userId}:{month.Year}-{month.Month:00}")));
}
