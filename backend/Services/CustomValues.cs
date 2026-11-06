using System.Globalization;
using System.Text.Json;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;

namespace ExpenseTracker.Api.Services;

// Typed values of custom columns. Stored as JSON primitives keyed by the column's stable key.
public static class CustomValues
{
    public const int MaxTextLength = 500;

    public static Dictionary<string, JsonElement> Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public static string Write(Dictionary<string, JsonElement> values) => JsonSerializer.Serialize(values);

    public static List<string> Options(CustomColumn column)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(column.OptionsJson) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    // Returns the normalised value, null to clear, or an error message.
    public static (JsonElement? Value, string? Error) Normalize(CustomColumn column, JsonElement raw)
    {
        if (raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return (null, null);
        if (raw.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(raw.GetString()) && column.DataType != "text")
            return (null, null);

        switch (column.DataType)
        {
            case "formula":
                return (null, $"“{column.Name}” is calculated and can't be typed into");

            case "text":
            {
                var text = raw.ValueKind == JsonValueKind.String ? raw.GetString()! : raw.GetRawText();
                if (text.Length > MaxTextLength) return (null, $"“{column.Name}” is limited to {MaxTextLength} characters");
                return text.Length == 0 ? (null, null) : (Element(text), null);
            }

            case "money":
            case "number":
            {
                if (!TryNumber(raw, out var number)) return (null, $"“{column.Name}” needs a number");
                if (column.DataType == "money" && !Money.HasValidScale(number))
                    return (null, $"“{column.Name}” allows at most 2 decimals");
                if (Math.Abs(number) > Money.Max) return (null, $"“{column.Name}” is too large");
                return (Element(number), null);
            }

            case "date":
            {
                var text = raw.ValueKind == JsonValueKind.String ? raw.GetString()! : string.Empty;
                if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    return (null, $"“{column.Name}” needs a date like 2026-11-05");
                return (Element(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), null);
            }

            case "checkbox":
            {
                if (raw.ValueKind is JsonValueKind.True or JsonValueKind.False) return (Element(raw.GetBoolean()), null);
                if (raw.ValueKind == JsonValueKind.String && bool.TryParse(raw.GetString(), out var b)) return (Element(b), null);
                return (null, $"“{column.Name}” is a checkbox (true/false)");
            }

            case "dropdown":
            {
                var text = raw.ValueKind == JsonValueKind.String ? raw.GetString()!.Trim() : raw.GetRawText();
                var option = Options(column).FirstOrDefault(o => o.Equals(text, StringComparison.OrdinalIgnoreCase));
                return option == null
                    ? (null, $"“{text}” isn't one of the options for “{column.Name}”")
                    : (Element(option), null);
            }

            default:
                return (null, $"Unknown column type {column.DataType}");
        }
    }

    public static bool TryNumber(JsonElement raw, out decimal number)
    {
        number = 0;
        if (raw.ValueKind == JsonValueKind.Number) return raw.TryGetDecimal(out number);
        if (raw.ValueKind == JsonValueKind.String)
            return decimal.TryParse(raw.GetString()!.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out number);
        return false;
    }

    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value);
}
