using Microsoft.Extensions.Options;

namespace ExpenseTracker.Api.Services;

public class CurrencyRatesOptions
{
    // Each rate is the value of one unit in a shared base currency (USD by default).
    public Dictionary<string, decimal> Rates { get; set; } = new();
}

public class CurrencyConverter
{
    private readonly CurrencyRatesOptions _options;

    public CurrencyConverter(IOptions<CurrencyRatesOptions> options)
    {
        _options = options.Value;
    }

    public IEnumerable<string> SupportedCurrencies => _options.Rates.Keys.OrderBy(k => k);

    public decimal Convert(decimal amount, string fromCurrency, string toCurrency)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
            return amount;

        if (!_options.Rates.TryGetValue(fromCurrency, out var fromRate)
            || !_options.Rates.TryGetValue(toCurrency, out var toRate)
            || toRate == 0)
            return amount;

        return Math.Round(amount * fromRate / toRate, 2);
    }
}
