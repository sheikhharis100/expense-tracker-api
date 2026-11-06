using System.Security.Claims;

namespace ExpenseTracker.Api.Services;

public static class AuthHelper
{
    public static int GetCurrentUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid token");
        }
        return userId;
    }
}

// Rate limiter policy names (configured in Program.cs).
public static class RateLimits
{
    public const string Auth = "auth";
}