using System.Globalization;
using System.Text;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;

namespace ExpenseTracker.Api.Services;

// Checks shared by every endpoint that accepts input, so the same mistake gets the same message everywhere.
// Each check returns null when the value is fine, otherwise a short message for the user.
public static class InputRules
{
    // Ledger dates and months. Anything outside is almost certainly a typo (20226, 1926…).
    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    // Text lengths, matching the DTO attributes and what the forms allow.
    public const int DescriptionLength = 500;
    public const int NoteLength = 1000;
    public const int NameLength = 100;
    public const int ColumnNameLength = 60;
    public const int OptionLength = 60;
    public const int SearchLength = 200;

    private static readonly string MaxText = Money.Max.ToString("N2", CultureInfo.InvariantCulture);

    public static bool IsInRange(DateOnly date) => date.Year is >= MinYear and <= MaxYear;

    public static string? Date(DateOnly date, string label = "Date")
    {
        if (date == default) return $"{label} is required.";
        return IsInRange(date) ? null : $"{label} must be between {MinYear} and {MaxYear}.";
    }

    public static string? Date(DateOnly? date, string label = "Date") => date is DateOnly d ? Date(d, label) : null;

    public static string? Month(YearMonth month, string label = "Month") =>
        month == default ? $"{label} is required."
        : YearMonth.IsValid(month.Year, month.Month) ? null
        : $"{label} must be between {MinYear} and {MaxYear}.";

    // Amounts of money that must be positive: entries, payments, refunds.
    public static string? Amount(decimal amount, string label = "Amount")
    {
        if (amount <= 0) return $"{label} must be greater than zero.";
        if (amount > Money.Max) return $"{label} can't be more than {MaxText}.";
        return Money.HasValidScale(amount) ? null : $"{label} can have at most 2 decimals.";
    }

    // Budgets, salary plans, reserved savings: zero is allowed.
    public static string? NonNegative(decimal amount, string label)
    {
        if (amount < 0) return $"{label} can't be negative.";
        return Signed(amount, label);
    }

    // Opening balances and adjustments can go either way.
    public static string? Signed(decimal amount, string label)
    {
        if (Math.Abs(amount) > Money.Max) return $"{label} can't be more than {MaxText}.";
        return Money.HasValidScale(amount) ? null : $"{label} can have at most 2 decimals.";
    }

    public static string? PaymentMethod(string? method) =>
        string.IsNullOrEmpty(method) || PaymentMethods.All.Contains(method)
            ? null
            : $"Payment method must be one of: {string.Join(", ", PaymentMethods.All)}.";

    // Every text field is a single line: control characters (newlines, tabs, NUL…) become spaces, then it's trimmed.
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (!text.Any(char.IsControl)) return text.Trim();
        var sb = new StringBuilder(text.Length);
        foreach (var c in text) sb.Append(char.IsControl(c) ? ' ' : c);
        return sb.ToString().Trim();
    }

    public static string? Text(string text, int max, string label) =>
        text.Length > max ? $"{label} can be at most {max} characters." : null;

    // A required name, already cleaned.
    public static string? Name(string name, int max, string label = "Name")
    {
        if (name.Length == 0) return $"{label} is required.";
        return Text(name, max, label);
    }
}
