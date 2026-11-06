namespace ExpenseTracker.Api.Ledger;

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

// "Today" and month boundaries are local calendar days in the user's zone (Asia/Karachi by default),
// never the server's clock or UTC.
public static class LocalCalendar
{
    public const string DefaultZone = "Asia/Karachi";

    private static readonly TimeZoneInfo KarachiFallback =
        TimeZoneInfo.CreateCustomTimeZone(DefaultZone, TimeSpan.FromHours(5), "Pakistan Standard Time", "PKT");

    public static bool IsValidZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return id == DefaultZone;
        }
    }

    public static TimeZoneInfo Zone(string? id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? DefaultZone : id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Pakistan has used UTC+5 without daylight saving since 2009.
            return KarachiFallback;
        }
    }

    public static DateOnly Today(IClock clock, string? zoneId) => ToLocalDate(clock.UtcNow, zoneId);

    public static DateOnly ToLocalDate(DateTime utc, string? zoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone(zoneId)));
}
