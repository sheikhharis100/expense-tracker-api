using Microsoft.Extensions.Options;

namespace ExpenseTracker.Api.Services;

public class CurrencyRatesOptions
{
    public string ReportingCurrency { get; set; } = "USD";
    public Dictionary<string, decimal> Rates { get; set; } = new();
}

public class CurrencyConverter
{
    private readonly CurrencyRatesOptions _options;

    public CurrencyConverter(IOptions<CurrencyRatesOptions> options)
    {
        _options = options.Value;
    }

    public string ReportingCurrency => _options.ReportingCurrency;

    public decimal ToReportingCurrency(decimal amount, string fromCurrency)
    {
        return _options.Rates.TryGetValue(fromCurrency, out var rate) ? amount * rate : amount;
    }
}
