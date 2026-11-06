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

    public async Task<SheetImportResultDto> ImportAsync(int userId, Stream stream)
    {
        using var reader = new StreamReader(stream);
        var parsed = SheetCsvParser.Parse(reader);

        var result = new SheetImportResultDto
        {
            RowsRead = parsed.RowsRead,
            CellsSkipped = parsed.CellsSkipped,
            Warnings = parsed.Warnings.Take(50).ToList()
        };

        if (parsed.Days.Count == 0 && parsed.Salaries.Count == 0)
            return result;

        var user = await _context.Users.FirstAsync(u => u.Id == userId);
        var account = await DefaultData.GetOrCreateDefaultAccountAsync(_context, user);
        var categories = await _context.Categories.Where(c => c.UserId == userId).ToListAsync();

        var byColumn = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in parsed.CategoryColumns.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var category = categories.FirstOrDefault(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));
            if (category == null)
            {
                category = new Category
                {
                    UserId = userId,
                    Name = column.Length > 100 ? column[..100] : column,
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
        var fromDate = parsed.Days.Count > 0 ? parsed.Days.Min(d => d.Date) : DateOnly.MinValue;
        var toDate = (parsed.Days.Count > 0 ? parsed.Days.Max(d => d.Date) : DateOnly.MinValue).AddDays(1);
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

        foreach (var day in parsed.Days)
        {
            var touched = false;
            foreach (var (column, amounts) in day.Cells)
            {
                var category = byColumn[column];
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

            if (day.Note.Length > 0)
            {
                var text = day.Note.Length > 1000 ? day.Note[..1000] : day.Note;
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
        var today = LocalCalendar.Today(_clock, user.TimeZone);
        var plans = await _context.SalaryPlans.Where(s => s.UserId == userId).ToListAsync();
        foreach (var hint in parsed.Salaries.OrderBy(s => s.PayMonth.Index))
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

        await _context.SaveChangesAsync();
        return result;
    }
}
