using System.Globalization;

namespace ExpenseTracker.Formulas.Evaluation;

/// <summary>Coercion and comparison rules shared by operators and functions.</summary>
internal static class ValueOps
{
    /// <summary>Longest text a formula may produce (same as an Excel cell).</summary>
    public const int MaxTextLength = 32_767;

    private const NumberStyles NumericTextStyle =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public static FormulaValue.Error Error(string code, string message) => new(code, message);

    /// <summary>"12", " -3.5 ", ".5" are numeric text; "1,000", "1e3", "" and "abc" are not.</summary>
    public static bool TryParseNumber(string s, out decimal value) =>
        decimal.TryParse(s, NumericTextStyle, CultureInfo.InvariantCulture, out value);

    /// <summary>Arithmetic coercion: blank = 0, TRUE = 1, FALSE = 0, numeric text is converted, other text is #VALUE!.</summary>
    public static FormulaValue.Error? ToNumber(FormulaValue v, out decimal value)
    {
        switch (v)
        {
            case FormulaValue.Number n:
                value = n.Value;
                return null;
            case FormulaValue.Blank:
                value = 0m;
                return null;
            case FormulaValue.Bool b:
                value = b.Value ? 1m : 0m;
                return null;
            case FormulaValue.Text t when TryParseNumber(t.Value, out value):
                return null;
            case FormulaValue.Text t:
                value = 0m;
                return Error(FormulaErrors.Value, $"\"{Shorten(t.Value)}\" is not a number.");
            case FormulaValue.Error e:
                value = 0m;
                return e;
            default:
                value = 0m;
                return Error(FormulaErrors.Value, "Unsupported value.");
        }
    }

    /// <summary>Text coercion: numbers without trailing zeros, TRUE/FALSE, blank = "".</summary>
    public static string ToText(FormulaValue v) => v switch
    {
        FormulaValue.Text t => t.Value,
        FormulaValue.Number n => FormulaValue.FormatNumber(n.Value),
        FormulaValue.Bool b => b.Value ? "TRUE" : "FALSE",
        _ => string.Empty,
    };

    /// <summary>Condition coercion for IF: number ≠ 0, TRUE/FALSE, text "TRUE"/"FALSE", blank = FALSE.</summary>
    public static FormulaValue.Error? ToCondition(FormulaValue v, out bool value)
    {
        value = false;
        switch (v)
        {
            case FormulaValue.Error e:
                return e;
            case FormulaValue.Bool b:
                value = b.Value;
                return null;
            case FormulaValue.Number n:
                value = n.Value != 0m;
                return null;
            case FormulaValue.Blank:
                return null;
            case FormulaValue.Text t when t.Value.Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase):
                value = true;
                return null;
            case FormulaValue.Text t when t.Value.Trim().Equals("FALSE", StringComparison.OrdinalIgnoreCase):
                return null;
            case FormulaValue.Text t:
                return Error(FormulaErrors.Value, $"IF condition \"{Shorten(t.Value)}\" is not TRUE or FALSE.");
            default:
                return Error(FormulaErrors.Value, "Unsupported value.");
        }
    }

    /// <summary>
    /// Compares two non-error values. A blank side takes the "empty" value of the other side's type
    /// (0, FALSE or ""). If both sides are numeric (numbers, TRUE/FALSE as 1/0, or numeric text) they
    /// compare as numbers; otherwise they compare as text, case-insensitively (ordinal).
    /// </summary>
    public static int Compare(FormulaValue a, FormulaValue b)
    {
        if (a is FormulaValue.Blank && b is FormulaValue.Blank)
        {
            return 0;
        }

        if (a is FormulaValue.Blank)
        {
            a = EmptyLike(b);
        }
        else if (b is FormulaValue.Blank)
        {
            b = EmptyLike(a);
        }

        if (TryNumericView(a, out var x) && TryNumericView(b, out var y))
        {
            return x.CompareTo(y);
        }

        return Math.Sign(string.Compare(ToText(a), ToText(b), StringComparison.OrdinalIgnoreCase));
    }

    public static bool Holds(BinaryOpKind op, int comparison) => op switch
    {
        BinaryOpKind.Equal => comparison == 0,
        BinaryOpKind.NotEqual => comparison != 0,
        BinaryOpKind.Less => comparison < 0,
        BinaryOpKind.Greater => comparison > 0,
        BinaryOpKind.LessOrEqual => comparison <= 0,
        BinaryOpKind.GreaterOrEqual => comparison >= 0,
        _ => false,
    };

    public static string Shorten(string s) => s.Length <= 40 ? s : s[..37] + "...";

    private static FormulaValue EmptyLike(FormulaValue other) => other switch
    {
        FormulaValue.Number => new FormulaValue.Number(0m),
        FormulaValue.Bool => FormulaValue.Bool.False,
        _ => new FormulaValue.Text(string.Empty),
    };

    internal static bool TryNumericView(FormulaValue v, out decimal value)
    {
        switch (v)
        {
            case FormulaValue.Number n:
                value = n.Value;
                return true;
            case FormulaValue.Bool b:
                value = b.Value ? 1m : 0m;
                return true;
            case FormulaValue.Text t:
                return TryParseNumber(t.Value, out value);
            default:
                value = 0m;
                return false;
        }
    }
}

/// <summary>The comparison operators (shared by the = &lt; &gt; ... operators and SUMIF criteria).</summary>
internal enum BinaryOpKind
{
    Equal,
    NotEqual,
    Less,
    Greater,
    LessOrEqual,
    GreaterOrEqual,
}

/// <summary>A parsed SUMIF/SUMIFS criterion such as <c>"&gt;100"</c>, <c>"&lt;&gt;Food"</c> or <c>50</c>.</summary>
internal readonly struct Criterion
{
    private readonly BinaryOpKind _op;
    private readonly FormulaValue _operand;

    private Criterion(BinaryOpKind op, FormulaValue operand)
    {
        _op = op;
        _operand = operand;
    }

    public static Criterion From(FormulaValue criteria)
    {
        switch (criteria)
        {
            case FormulaValue.Number or FormulaValue.Bool:
                return new Criterion(BinaryOpKind.Equal, criteria);
            case FormulaValue.Text t:
            {
                var s = t.Value;
                (BinaryOpKind op, int length) = s switch
                {
                    _ when s.StartsWith("<=", StringComparison.Ordinal) => (BinaryOpKind.LessOrEqual, 2),
                    _ when s.StartsWith(">=", StringComparison.Ordinal) => (BinaryOpKind.GreaterOrEqual, 2),
                    _ when s.StartsWith("<>", StringComparison.Ordinal) => (BinaryOpKind.NotEqual, 2),
                    _ when s.StartsWith('<') => (BinaryOpKind.Less, 1),
                    _ when s.StartsWith('>') => (BinaryOpKind.Greater, 1),
                    _ when s.StartsWith('=') => (BinaryOpKind.Equal, 1),
                    _ => (BinaryOpKind.Equal, 0),
                };
                var operand = length == 0 ? s : s[length..].Trim();
                return new Criterion(op, new FormulaValue.Text(operand));
            }

            default:
                // Blank criteria behaves like "" (matches blank and empty cells).
                return new Criterion(BinaryOpKind.Equal, new FormulaValue.Text(string.Empty));
        }
    }

    /// <summary>
    /// Blank cells only match <c>""</c> and <c>"="</c>. With &lt;, &gt;, &lt;= or &gt;= and a numeric operand
    /// (e.g. <c>"&gt;100"</c>) only numeric cells can match, so a word such as "n/a" never satisfies "&gt;5".
    /// Errors must be handled by the caller.
    /// </summary>
    public bool Matches(FormulaValue cell)
    {
        if (cell is FormulaValue.Blank)
        {
            return _op == BinaryOpKind.Equal && _operand is FormulaValue.Text { Value.Length: 0 };
        }

        if (_op is BinaryOpKind.Less or BinaryOpKind.Greater or BinaryOpKind.LessOrEqual or BinaryOpKind.GreaterOrEqual
            && ValueOps.TryNumericView(_operand, out _)
            && !ValueOps.TryNumericView(cell, out _))
        {
            return false;
        }

        return ValueOps.Holds(_op, ValueOps.Compare(cell, _operand));
    }
}
