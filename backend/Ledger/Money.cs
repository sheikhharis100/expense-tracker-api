namespace ExpenseTracker.Api.Ledger;

// All money is System.Decimal (exact base-10), stored as numeric(18,2). Rounding happens only here, explicitly.
public static class Money
{
    public const int Decimals = 2;
    public const decimal Max = 9_999_999_999_999.99m;

    public static decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);

    // Conservative rounding for allowances: never suggest a paisa more than is really available.
    public static decimal RoundDown(decimal value) => Math.Floor(value * 100m) / 100m;

    public static bool HasValidScale(decimal value) => Math.Round(value, Decimals) == value;

    public static bool IsValidAmount(decimal value) => value > 0 && value <= Max && HasValidScale(value);

    // part / whole × 100 to one decimal place; null when the base is zero (shown as "N/A").
    public static decimal? Percent(decimal part, decimal whole) =>
        whole == 0 ? null : Math.Round(part / whole * 100m, 1, MidpointRounding.AwayFromZero);
}
