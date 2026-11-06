using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using CsvHelper;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;

namespace ExpenseTracker.Api.Services;

// Exports use the same calculator results as the screens. Every file states its reporting month and basis.
public class ExportService
{
    private readonly SummaryService _summary;
    private readonly ExpenseService _expenses;
    private readonly IncomeService _income;

    public ExportService(SummaryService summary, ExpenseService expenses, IncomeService income)
    {
        _summary = summary;
        _expenses = expenses;
        _income = income;
    }

    public sealed record ExportFile(byte[] Content, string ContentType, string FileName);

    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // ---------- Month ----------

    public async Task<ExportFile> MonthAsync(int userId, YearMonth month, string format, string part, string basis)
    {
        var summary = await _summary.MonthAsync(userId, month);
        var ledger = await _expenses.ListAsync(userId, month, new ExpenseFilterDto());
        var income = await _income.MonthAsync(userId, month);

        var summaryRows = basis == CashFlowFigures.Basis ? CashFlowRows(summary) : SummaryRows(summary);
        var entryTable = EntryTable(ledger);
        var incomeTable = IncomeTable(income);
        var categoryTable = CategoryTable(summary.Figures);

        if (format == "xlsx")
        {
            using var book = new XLWorkbook();
            AddKeyValueSheet(book, "Summary", month, basis, summaryRows);
            if (basis == MonthFigures.Basis) AddTableSheet(book, "Budget vs actual", month, basis, categoryTable);
            AddTableSheet(book, "Entries", month, basis, entryTable);
            AddTableSheet(book, "Income", month, basis, incomeTable);
            return new ExportFile(Save(book), XlsxType, $"expenses-{month}-{basis}.xlsx");
        }

        var (header, rows) = part switch
        {
            "entries" => entryTable,
            "income" => incomeTable,
            "categories" => categoryTable,
            _ => (new[] { "Item", "Value" }, summaryRows.Select(r => new[] { r.Label, Format(r.Value) }).ToList())
        };
        return new ExportFile(Csv(month.ToString(), basis, header, rows), "text/csv", $"{part}-{month}-{basis}.csv");
    }

    private static List<(string Label, object? Value)> SummaryRows(MonthSummaryDto s)
    {
        var f = s.Figures;
        return new()
        {
            ("Currency", s.Currency),
            ("Month status", f.Timing),
            ("Expected net salary", f.ExpectedSalary),
            ("Salary received toward it", f.SalaryReceivedTowardFunding),
            ("Salary outstanding", f.SalaryOutstanding),
            ("Salary overpaid", f.SalaryOverpaid),
            ("Salary income allocated to month", f.SalaryIncome),
            ("Other received income", f.OtherIncome),
            ("Total received income", f.ReceivedIncome),
            ("Opening balance", f.OpeningBalance),
            ("Available funds", f.AvailableFunds),
            ("Gross spending", f.GrossSpending),
            ("Refunds", f.Refunds),
            ("Net spending", f.NetSpending),
            ("Additional expenses (already included in net spending)", f.AdditionalNet),
            ("Remaining balance", f.RemainingBalance),
            ("Monthly surplus / deficit", f.Surplus),
            ("Total budget", f.TotalBudget?.ToString(CultureInfo.InvariantCulture) ?? "Not set"),
            ("Overall budget remaining", f.OverallBudgetRemaining?.ToString(CultureInfo.InvariantCulture) ?? "Not set"),
            ("Spending without a budget", f.UnbudgetedSpending),
            ("Income used %", f.IncomeUsedPercent?.ToString(CultureInfo.InvariantCulture) ?? "N/A"),
            ("Average daily spending", f.AverageDailySpending?.ToString(CultureInfo.InvariantCulture) ?? "N/A"),
            ("Days with spending", f.DaysWithSpending),
            ("Unpaid planned commitments", f.UnpaidCommitments),
            ("Reserved savings", f.ReservedSavings),
            ("Daily spending allowance", f.DailyAllowance?.ToString(CultureInfo.InvariantCulture) ?? "N/A"),
            ("Previous month net spending", s.Previous.NetSpending),
            ("Change vs previous month", s.Previous.NetSpendingChange),
        };
    }

    private static List<(string Label, object? Value)> CashFlowRows(MonthSummaryDto s) => new()
    {
        ("Currency", s.Currency),
        ("Salary received in month (by receipt date)", s.CashFlow.SalaryReceived),
        ("Other income received in month (by receipt date)", s.CashFlow.OtherReceived),
        ("Total received", s.CashFlow.TotalReceived),
        ("Net spending", s.CashFlow.NetSpending),
        ("Net cash flow", s.CashFlow.NetCashFlow),
    };

    private static (string[] Header, List<string[]> Rows) CategoryTable(MonthFigures f) => (
        new[] { "Category", "Budget", "Gross", "Refunds", "Net", "Budget remaining", "% of budget" },
        f.Categories.Select(c => new[]
        {
            c.Name, c.Budget.HasValue ? Format(c.Budget) : "Not set", Format(c.Gross), Format(c.Refunds), Format(c.Net),
            c.BudgetRemaining.HasValue ? Format(c.BudgetRemaining) : "", c.PercentOfBudget.HasValue ? Format(c.PercentOfBudget) : ""
        }).ToList());

    private static (string[] Header, List<string[]> Rows) EntryTable(ExpenseListDto ledger)
    {
        var custom = ledger.Columns.Where(c => !c.BuiltIn && !c.IsArchived).ToList();
        var header = new[] { "Id", "Date", "Type", "Status", "Category", "Description", "Amount", "Counts toward net spending", "Payment method", "Additional", "Recurring", "Refund of" }
            .Concat(custom.Select(c => c.Name)).ToArray();
        var rows = ledger.Rows.Select(r =>
        {
            var signed = r.Status != TxStatus.Paid ? 0 : r.Kind switch { TxKind.Expense => r.Amount, TxKind.Refund => -r.Amount, _ => 0 };
            var cells = new List<string>
            {
                r.Id.ToString(CultureInfo.InvariantCulture), r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.Kind, r.Status,
                r.CategoryName, r.Description, Format(r.Amount), Format(signed), r.PaymentMethod, r.IsAdditional ? "Yes" : "No",
                r.IsRecurring ? "Yes" : "No", r.RefundOfId?.ToString(CultureInfo.InvariantCulture) ?? ""
            };
            foreach (var c in custom)
            {
                if (c.Type == "formula")
                    cells.Add(r.Computed.TryGetValue(c.Key, out var v) ? v.Display ?? "" : "");
                else
                    cells.Add(r.Custom.TryGetValue(c.Key, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText()) : "");
            }
            return cells.ToArray();
        }).ToList();
        rows.Add(new[] { "", "", "", "", "", "Net spending (calculator)", "", Format(ledger.MonthTotals.Net) });
        return (header, rows);
    }

    private static (string[] Header, List<string[]> Rows) IncomeTable(IncomeMonthDto income)
    {
        var header = new[] { "Kind", "Salary earned for", "Category", "Status", "Received date", "Amount", "Allocated to this month", "Description" };
        var rows = new List<string[]>();
        foreach (var s in income.Funding)
            foreach (var p in s.Payments)
                rows.Add(new[] { "Salary", s.Earned.ToString(), "Salary", p.Status, p.ReceivedDate?.ToString("yyyy-MM-dd") ?? "", Format(p.Amount),
                    Format(p.Allocations.Where(a => a.Month == income.Month).Sum(a => a.Amount)), p.Description });
        foreach (var r in income.OtherIncome)
            rows.Add(new[] { "Other", "", r.CategoryName, r.Status, r.ReceivedDate?.ToString("yyyy-MM-dd") ?? "", Format(r.Amount),
                r.Status == ReceiptStatus.Received ? Format(r.Allocations.Where(a => a.Month == income.Month).Sum(a => a.Amount)) : "0 (expected)", r.Description });
        rows.Add(new[] { "Total received income (calculator)", "", "", "", "", "", Format(income.TotalReceivedIncome), "" });
        return (header, rows);
    }

    // ---------- Year ----------

    public async Task<ExportFile> YearAsync(int userId, int year, string format)
    {
        var annual = await _summary.YearAsync(userId, year);
        var header = new[] { "Month", "Status", "Expected salary", "Salary income", "Other income", "Received income", "Opening balance",
            "Gross spending", "Refunds", "Net spending", "Additional (included)", "Surplus", "Remaining balance", "Income used %", "Budget", "Unpaid commitments" };
        string[] Row(AnnualRowDto r, string label) => new[]
        {
            label, r.Timing, Format(r.ExpectedSalary), Format(r.SalaryIncome), Format(r.OtherIncome), Format(r.ReceivedIncome), Format(r.OpeningBalance),
            Format(r.GrossSpending), Format(r.Refunds), Format(r.NetSpending), Format(r.AdditionalNet), Format(r.Surplus), Format(r.RemainingBalance),
            r.IncomeUsedPercent.HasValue ? Format(r.IncomeUsedPercent) : "N/A", r.TotalBudget.HasValue ? Format(r.TotalBudget) : "Not set", Format(r.UnpaidCommitments)
        };
        var rows = annual.Rows.Select(r => Row(r, r.Month.ToString())).ToList();
        rows.Add(Row(annual.Totals, "Total"));

        if (format == "xlsx")
        {
            using var book = new XLWorkbook();
            AddTableSheet(book, "Annual", null, MonthFigures.Basis, (header, rows), $"Year {year}");
            AddTableSheet(book, "Categories", null, MonthFigures.Basis,
                (new[] { "Category", "Net spending" }, annual.Categories.Select(c => new[] { c.Name, Format(c.Net) }).ToList()), $"Year {year}");
            return new ExportFile(Save(book), XlsxType, $"annual-{year}.xlsx");
        }
        return new ExportFile(Csv($"{year}", MonthFigures.Basis, header, rows), "text/csv", $"annual-{year}.csv");
    }

    // ---------- Writers ----------

    private static string Format(object? value) => value switch
    {
        null => "",
        decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private static byte[] Csv(string reportingMonth, string basis, string[] header, List<string[]> rows)
    {
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteField("Reporting month");
            csv.WriteField("Accounting basis");
            foreach (var h in header) csv.WriteField(h);
            csv.NextRecord();
            foreach (var row in rows)
            {
                csv.WriteField(reportingMonth);
                csv.WriteField(basis);
                foreach (var cell in row) csv.WriteField(cell);
                csv.NextRecord();
            }
        }
        return stream.ToArray();
    }

    private static void AddKeyValueSheet(XLWorkbook book, string name, YearMonth month, string basis, List<(string Label, object? Value)> rows)
    {
        var sheet = book.Worksheets.Add(name);
        sheet.Cell(1, 1).Value = "Reporting month";
        sheet.Cell(1, 2).Value = month.ToString();
        sheet.Cell(2, 1).Value = "Accounting basis";
        sheet.Cell(2, 2).Value = basis;
        var r = 4;
        foreach (var (label, value) in rows)
        {
            sheet.Cell(r, 1).Value = label;
            SetCell(sheet.Cell(r, 2), value);
            r++;
        }
        sheet.Columns().AdjustToContents();
    }

    private static void AddTableSheet(XLWorkbook book, string name, YearMonth? month, string basis, (string[] Header, List<string[]> Rows) table, string? period = null)
    {
        var sheet = book.Worksheets.Add(name);
        sheet.Cell(1, 1).Value = "Reporting period";
        sheet.Cell(1, 2).Value = period ?? month?.ToString() ?? "";
        sheet.Cell(2, 1).Value = "Accounting basis";
        sheet.Cell(2, 2).Value = basis;
        for (var c = 0; c < table.Header.Length; c++)
        {
            sheet.Cell(4, c + 1).Value = table.Header[c];
            sheet.Cell(4, c + 1).Style.Font.Bold = true;
        }
        for (var r = 0; r < table.Rows.Count; r++)
            for (var c = 0; c < table.Rows[r].Length; c++)
                SetCell(sheet.Cell(r + 5, c + 1), table.Rows[r][c]);
        sheet.Columns().AdjustToContents();
    }

    private static void SetCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case decimal d:
                cell.Value = d;
                cell.Style.NumberFormat.Format = "#,##0.00";
                break;
            case int i:
                cell.Value = i;
                break;
            case string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && s.Contains('.'):
                cell.Value = n;
                cell.Style.NumberFormat.Format = "#,##0.00";
                break;
            default:
                cell.Value = value?.ToString() ?? "";
                break;
        }
    }

    private static byte[] Save(XLWorkbook book)
    {
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }
}
