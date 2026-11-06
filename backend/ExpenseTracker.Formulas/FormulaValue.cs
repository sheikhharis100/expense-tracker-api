using System.Globalization;

namespace ExpenseTracker.Formulas;

/// <summary>The error codes a formula can produce.</summary>
public static class FormulaErrors
{
    /// <summary>Division by zero, or AVERAGE of no numbers.</summary>
    public const string DivideByZero = "#DIV/0!";

    /// <summary>A value of the wrong type (e.g. text in arithmetic) or an invalid argument.</summary>
    public const string Value = "#VALUE!";

    /// <summary>A reference to a column key that does not exist (e.g. a deleted column).</summary>
    public const string Ref = "#REF!";

    /// <summary>An unknown function or an unknown column display name.</summary>
    public const string Name = "#NAME?";

    /// <summary>A circular reference between formula columns.</summary>
    public const string Circular = "#CIRC!";

    /// <summary>A number that is too large (decimal overflow).</summary>
    public const string Num = "#NUM!";

    /// <summary>The formula text could not be parsed.</summary>
    public const string Syntax = "#SYNTAX!";
}

/// <summary>
/// A value produced or consumed by a formula: <see cref="Number"/>, <see cref="Text"/>,
/// <see cref="Bool"/>, <see cref="Blank"/> or <see cref="Error"/>.
/// </summary>
public abstract record FormulaValue
{
    private FormulaValue()
    {
    }

    /// <summary>A number. Always a <see cref="decimal"/>, never floating point.</summary>
    public sealed record Number(decimal Value) : FormulaValue;

    /// <summary>A piece of text. Dates are passed as ISO <c>yyyy-MM-dd</c> text.</summary>
    public sealed record Text(string Value) : FormulaValue
    {
        public string Value { get; init; } = Value ?? string.Empty;
    }

    /// <summary>TRUE or FALSE.</summary>
    public sealed record Bool(bool Value) : FormulaValue
    {
        public static Bool True { get; } = new(true);
        public static Bool False { get; } = new(false);

        public static Bool Of(bool value) => value ? True : False;
    }

    /// <summary>An empty cell.</summary>
    public sealed record Blank : FormulaValue
    {
        public static Blank Instance { get; } = new();
    }

    /// <summary>An error such as <c>#DIV/0!</c>; see <see cref="FormulaErrors"/> for the codes.</summary>
    public sealed record Error(string Code, string Message) : FormulaValue
    {
        public string Code { get; init; } = Code ?? FormulaErrors.Value;
        public string Message { get; init; } = Message ?? string.Empty;
    }

    /// <summary>True when this value is an <see cref="Error"/>.</summary>
    public bool IsError => this is Error;

    /// <summary>True when this value is <see cref="Blank"/>.</summary>
    public bool IsBlank => this is Blank;

    /// <summary>
    /// The text shown in a table cell: numbers in invariant culture without trailing zeros
    /// (<c>3.00</c> → <c>3</c>), TRUE/FALSE, blank as empty text, errors as their code.
    /// </summary>
    public string AsDisplayString() => this switch
    {
        Number n => FormatNumber(n.Value),
        Text t => t.Value,
        Bool b => b.Value ? "TRUE" : "FALSE",
        Error e => e.Code,
        _ => string.Empty,
    };

    /// <summary>Creates a number, or <see cref="Blank"/> for null.</summary>
    public static FormulaValue FromNumber(decimal? value) =>
        value is { } v ? new Number(v) : Blank.Instance;

    /// <summary>Creates text, or <see cref="Blank"/> for null or empty text.</summary>
    public static FormulaValue FromText(string? value) =>
        string.IsNullOrEmpty(value) ? Blank.Instance : new Text(value);

    /// <summary>Creates a boolean, or <see cref="Blank"/> for null.</summary>
    public static FormulaValue FromBool(bool? value) =>
        value is { } v ? Bool.Of(v) : Blank.Instance;

    internal static string FormatNumber(decimal value)
    {
        if (value == 0m)
        {
            return "0";
        }

        // decimal.ToString never uses exponent notation; just drop trailing zeros.
        var s = value.ToString(CultureInfo.InvariantCulture);
        if (s.Contains('.'))
        {
            s = s.TrimEnd('0').TrimEnd('.');
        }

        return s;
    }
}
