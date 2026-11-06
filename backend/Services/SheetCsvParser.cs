using System.Globalization;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;

namespace ExpenseTracker.Api.Services;

// Reads the "Daily Log" tab of the Google Sheet (File → Download → CSV).
// Each category column holds the day's total; the "Expense details" column lists the separate amounts
// ("Food: 1810 + 1000"), which are used whenever they add up to the cell.
public static class SheetCsvParser
{
    public record ParsedDay(DateOnly Date, Dictionary<string, List<decimal>> Cells, string Note);
    public record SalaryHint(YearMonth PayMonth, decimal Amount);

    public class ParseResult
    {
        public List<string> CategoryColumns { get; } = new();
        public List<ParsedDay> Days { get; } = new();
        public List<SalaryHint> Salaries { get; } = new();
        public List<string> Warnings { get; } = new();
        public int RowsRead { get; set; }
        public int CellsSkipped { get; set; }
    }

    private static readonly string[] DateFormats =
    {
        "dd MMM yyyy", "d MMM yyyy", "yyyy-MM-dd", "dd-MMM-yyyy", "d-MMM-yyyy",
        "dd/MM/yyyy", "d/M/yyyy", "ddd, dd MMM yyyy", "dddd, d MMMM yyyy", "d MMMM yyyy"
    };

    private static readonly HashSet<string> LeadingHeaders = new(StringComparer.OrdinalIgnoreCase) { "Date", "Day", "Month" };
    private static readonly HashSet<string> TotalHeaders = new(StringComparer.OrdinalIgnoreCase) { "Daily Total", "Total", "Day Total" };

    private static readonly Regex MonthSectionPattern = new(@"^(?<month>[A-Za-z]{3,9})\s+(?<year>\d{4})$", RegexOptions.Compiled);
    private static readonly Regex SalaryPattern = new(
        @"Salary\s*\((?<month>[A-Za-z]{3,9})\s+pay\)\s*:\s*(?<amount>[\d,]+(?:\.\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParseResult Parse(TextReader reader)
    {
        var result = new ParseResult();
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { BadDataFound = null, DetectColumnCountChanges = false };
        using var csv = new CsvParser(reader, config);

        var dateCol = -1;
        var detailsCol = -1;
        var noteCol = -1;
        var categoryCols = new List<(int Index, string Name)>();
        YearMonth? section = null;

        while (csv.Read())
        {
            var row = csv.Record ?? Array.Empty<string>();
            string Cell(int i) => i >= 0 && i < row.Length ? row[i].Trim() : string.Empty;

            if (dateCol < 0)
            {
                dateCol = Array.FindIndex(row, c => c.Trim().Equals("Date", StringComparison.OrdinalIgnoreCase));
                if (dateCol >= 0)
                    ReadHeader(row, dateCol, categoryCols, ref detailsCol, ref noteCol, result);
                continue;
            }

            if (row.All(string.IsNullOrWhiteSpace) || Cell(dateCol).Equals("Date", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryParseDate(Cell(dateCol), out var date))
            {
                // Month section rows ("NOVEMBER 2026") also carry that month's salary line.
                var sectionMatch = MonthSectionPattern.Match(Cell(dateCol));
                if (sectionMatch.Success && TryParseMonthName(sectionMatch.Groups["month"].Value, out var sectionMonth))
                    section = new YearMonth(int.Parse(sectionMatch.Groups["year"].Value), sectionMonth);

                ReadSalaryHint(row, section, result);
                continue;
            }

            result.RowsRead++;
            var details = ParseDetails(Cell(detailsCol));
            var cells = new Dictionary<string, List<decimal>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (index, name) in categoryCols)
            {
                var raw = Cell(index);
                if (IsBlank(raw))
                    continue;

                if (!TryParseAmount(raw, out var total) || total < 0 || (total > 0 && InputRules.Amount(total) != null))
                {
                    result.CellsSkipped++;
                    result.Warnings.Add($"{date:dd MMM yyyy} · {name}: couldn't read \"{raw}\"");
                    continue;
                }

                if (total == 0)
                    continue;

                cells[name] = details.TryGetValue(name, out var parts) && parts.Sum() == total
                    ? parts
                    : new List<decimal> { total };
            }

            var note = Cell(noteCol);
            if (cells.Count > 0 || note.Length > 0)
                result.Days.Add(new ParsedDay(date, cells, note));
        }

        if (dateCol < 0)
            result.Warnings.Add("Couldn't find the header row. Export the \"Daily Log\" tab, which has a \"Date\" column.");
        else if (categoryCols.Count == 0)
            result.Warnings.Add("Found the header row but no spending columns between \"Month\" and \"Daily Total\".");

        return result;
    }

    private static void ReadHeader(string[] row, int dateCol, List<(int, string)> categoryCols,
        ref int detailsCol, ref int noteCol, ParseResult result)
    {
        var inCategories = true;
        for (var i = dateCol + 1; i < row.Length; i++)
        {
            var header = row[i].Trim();
            if (header.Length == 0)
                continue;

            if (header.StartsWith("Expense details", StringComparison.OrdinalIgnoreCase) || header.Equals("Details", StringComparison.OrdinalIgnoreCase))
            {
                detailsCol = i;
                inCategories = false;
            }
            else if (header.Equals("Remarks", StringComparison.OrdinalIgnoreCase) || header.StartsWith("Note", StringComparison.OrdinalIgnoreCase))
            {
                noteCol = i;
                inCategories = false;
            }
            else if (TotalHeaders.Contains(header))
            {
                inCategories = false;
            }
            else if (inCategories && !LeadingHeaders.Contains(header))
            {
                categoryCols.Add((i, header));
                result.CategoryColumns.Add(header);
            }
        }
    }

    private static void ReadSalaryHint(string[] row, YearMonth? section, ParseResult result)
    {
        if (section is not YearMonth month)
            return;

        foreach (var cell in row)
        {
            var match = SalaryPattern.Match(cell);
            if (!match.Success
                || !TryParseMonthName(match.Groups["month"].Value, out var payMonthNumber)
                || !TryParseAmount(match.Groups["amount"].Value, out var amount)
                || InputRules.Amount(amount) != null)
                continue;

            // "Oct pay" inside the November section is the most recent October at or before November.
            var payMonth = Enumerable.Range(0, 12).Select(k => month.AddMonths(-k)).First(m => m.Month == payMonthNumber);
            if (!result.Salaries.Any(s => s.PayMonth == payMonth))
                result.Salaries.Add(new SalaryHint(payMonth, amount));
            return;
        }
    }

    // "Bill / Rent: 14,920  |  Food: 625  |  Other Expenses: 2000 + 400"
    public static Dictionary<string, List<decimal>> ParseDetails(string details)
    {
        var result = new Dictionary<string, List<decimal>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(details))
            return result;

        foreach (var piece in details.Split('|'))
        {
            var colon = piece.LastIndexOf(':');
            if (colon <= 0)
                continue;

            var name = piece[..colon].Trim();
            var amounts = new List<decimal>();
            foreach (var part in piece[(colon + 1)..].Split('+'))
            {
                if (!TryParseAmount(part, out var amount) || InputRules.Amount(amount) != null)
                {
                    amounts.Clear();
                    break;
                }
                amounts.Add(amount);
            }

            if (name.Length > 0 && amounts.Count > 0)
                result[name] = amounts;
        }

        return result;
    }

    public static bool TryParseAmount(string raw, out decimal amount)
    {
        var cleaned = raw.Trim().TrimStart('=')
            .Replace(",", string.Empty)
            .Replace("PKR", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Rs.", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Rs", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static bool IsBlank(string raw) => raw.Length == 0 || raw is "-" or "–" or "—";

    private static bool TryParseDate(string raw, out DateOnly date) =>
        DateOnly.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date);

    private static bool TryParseMonthName(string name, out int month)
    {
        month = 0;
        if (name.Length < 3)
            return false;

        var prefix = name[..3];
        var names = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames;
        var index = Array.FindIndex(names, n => n.Equals(prefix, StringComparison.OrdinalIgnoreCase));
        if (index is < 0 or > 11)
            return false;

        month = index + 1;
        return true;
    }
}
