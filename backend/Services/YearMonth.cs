using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExpenseTracker.Api.Services;

[JsonConverter(typeof(YearMonthJsonConverter))]
public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public int Index => Year * 12 + (Month - 1);
    public DateTime FirstDay => new(Year, Month, 1);
    public DateTime NextFirstDay => FirstDay.AddMonths(1);
    // Real calendar lengths, including 29 February in leap years.
    public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

    public static YearMonth FromIndex(int index) => new(index / 12, index % 12 + 1);
    public static YearMonth Of(DateTime date) => new(date.Year, date.Month);
    public static YearMonth Of(DateOnly date) => new(date.Year, date.Month);

    public YearMonth AddMonths(int months) => FromIndex(Index + months);

    public static bool IsValid(int year, int month) => year is >= 2000 and <= 3000 && month is >= 1 and <= 12;

    // Accepts "2026-11".
    public static bool TryParse(string? value, out YearMonth result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return false;
        result = Of(d);
        return IsValid(result.Year, result.Month);
    }

    public int CompareTo(YearMonth other) => Index.CompareTo(other.Index);
    public static bool operator <(YearMonth a, YearMonth b) => a.Index < b.Index;
    public static bool operator >(YearMonth a, YearMonth b) => a.Index > b.Index;
    public static bool operator <=(YearMonth a, YearMonth b) => a.Index <= b.Index;
    public static bool operator >=(YearMonth a, YearMonth b) => a.Index >= b.Index;

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}

// Months travel as "2026-11" in JSON.
public sealed class YearMonthJsonConverter : JsonConverter<YearMonth>
{
    public override YearMonth Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        YearMonth.TryParse(reader.GetString(), out var ym) ? ym : throw new JsonException("Month must look like 2026-11");

    public override void Write(Utf8JsonWriter writer, YearMonth value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
