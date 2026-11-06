using ExpenseTracker.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace ExpenseTracker.Api.Services;

public static class LedgerScopes
{
    public const string Entries = "entries";
    public const string Notes = "notes";
    public const string Salary = "salary";
    public const string Adjustment = "adjustment";
    public const string Categories = "categories";
    public const string Profile = "profile";
    public const string Import = "import";
    public const string Budgets = "budgets";
    public const string Recurring = "recurring";
    public const string Columns = "columns";
    public const string Settings = "settings";
    // The account's sessions were reset: other devices refresh and find their token refused.
    public const string Session = "session";
}

// Origin is the X-Client-Id header of the request that made the change, so a device can skip its own echo.
public record LedgerChange(string Scope, int? Year, int? Month, string? Origin);

public class LedgerNotifier
{
    public const string EventName = "ledgerChanged";

    private readonly IHubContext<LedgerHub> _hub;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<LedgerNotifier> _logger;

    public LedgerNotifier(IHubContext<LedgerHub> hub, IHttpContextAccessor http, ILogger<LedgerNotifier> logger)
    {
        _hub = hub;
        _http = http;
        _logger = logger;
    }

    public async Task NotifyAsync(int userId, string scope, YearMonth? month = null)
    {
        var origin = _http.HttpContext?.Request.Headers["X-Client-Id"].FirstOrDefault();
        var change = new LedgerChange(scope, month?.Year, month?.Month, origin);

        try
        {
            await _hub.Clients.User(userId.ToString()).SendAsync(EventName, change);
        }
        catch (Exception ex)
        {
            // The write already succeeded; a missed push only means other devices refresh later.
            _logger.LogWarning(ex, "Could not push {Scope} change to user {UserId}", scope, userId);
        }
    }
}
