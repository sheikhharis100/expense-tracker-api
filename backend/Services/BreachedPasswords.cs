using System.Security.Cryptography;
using System.Text;

namespace ExpenseTracker.Api.Services;

public interface IBreachedPasswordChecker
{
    // True when the password is in a known breach. Never throws: when the check can't run, the answer is false.
    Task<bool> IsBreachedAsync(string password, CancellationToken ct = default);
}

// Have I Been Pwned's Pwned Passwords range API (free, no key). Only the first 5 hex characters of the password's
// SHA-1 leave the server (k-anonymity); the matching is done here, and padding hides how many matches came back.
public sealed class PwnedPasswordsChecker : IBreachedPasswordChecker
{
    public const string BaseAddress = "https://api.pwnedpasswords.com/";

    private readonly HttpClient _http;
    private readonly ILogger<PwnedPasswordsChecker> _logger;

    public PwnedPasswordsChecker(HttpClient http, ILogger<PwnedPasswordsChecker> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<bool> IsBreachedAsync(string password, CancellationToken ct = default)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var prefix = hash[..5];
        var suffix = hash[5..];
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{prefix}");
            request.Headers.Add("Add-Padding", "true");
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(ct);

            // Lines look like "0018A45C4D1DEF81644B54AB7F969B88D65:21"; padding lines have a count of 0.
            foreach (var line in body.Split('\n'))
            {
                var parts = line.Trim().Split(':');
                if (parts.Length == 2 && parts[0].Equals(suffix, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(parts[1], out var count) && count > 0)
                    return true;
            }
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Fail open: a slow or unreachable service mustn't stop people from signing up or resetting a password.
            _logger.LogWarning(ex, "Breached-password check unavailable; the password was accepted without it");
            return false;
        }
    }
}

// Security:BreachCheck = false.
public sealed class NoBreachCheck : IBreachedPasswordChecker
{
    public Task<bool> IsBreachedAsync(string password, CancellationToken ct = default) => Task.FromResult(false);
}
