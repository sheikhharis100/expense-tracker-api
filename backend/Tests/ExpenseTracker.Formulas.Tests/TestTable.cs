using System.Globalization;

namespace ExpenseTracker.Formulas.Tests;

/// <summary>Builds a small table (base columns + formula columns) for evaluating formulas in tests.</summary>
internal sealed class TestTable
{
    public const string ProbeKey = "probe";

    private readonly List<KeyValuePair<string, string>> _names = new();
    private readonly Dictionary<string, FormulaValue[]> _base = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _formulaText = new(StringComparer.Ordinal);

    public TestTable(int rows) => Rows = rows;

    public int Rows { get; }

    /// <summary>How many times each base column getter has been called (to check memoisation).</summary>
    public Dictionary<string, int> BaseCalls { get; } = new(StringComparer.Ordinal);

    public Func<string, string?> Resolver => FormulaCompiler.CreateNameResolver(_names);

    public string? NameOf(string key) => _names.FirstOrDefault(n => n.Key == key).Value;

    /// <summary>
    /// The standard ledger used by many tests:
    /// <code>
    /// row amount  category   date        description status  Qty(c1) Tip %(c2) Code(c3)
    /// 0   50      Food       2026-11-01  Lunch       paid    2       10        "100"
    /// 1   150.25  Rent       2026-11-03  Flat        paid    1       (blank)   "200"
    /// 2   200     food       2026-11-10  Party       pending 0       15        "100"
    /// 3   20      Transport  2026-11-15  Bus         paid    (blank) "n/a"     "abc"
    /// 4   (blank) (blank)    2026-11-20  Misc        pending "3"     5         (blank)
    /// </code>
    /// Total amount = 420.25.
    /// </summary>
    public static TestTable Ledger() => new TestTable(5)
        .Column("amount", "Amount", 50m, 150.25m, 200m, 20m, null)
        .Column("category", "Category", "Food", "Rent", "food", "Transport", null)
        .Column("date", "Date", "2026-11-01", "2026-11-03", "2026-11-10", "2026-11-15", "2026-11-20")
        .Column("description", "Description", "Lunch", "Flat", "Party", "Bus", "Misc")
        .Column("status", "Status", "paid", "paid", "pending", "paid", "pending")
        .Column("c1", "Qty", 2m, 1m, 0m, null, "3")
        .Column("c2", "Tip %", 10m, null, 15m, "n/a", 5m)
        .Column("c3", "Code", "100", "200", "100", "abc", null);

    public static FormulaValue V(object? o) => o switch
    {
        null => FormulaValue.Blank.Instance,
        FormulaValue v => v,
        decimal d => new FormulaValue.Number(d),
        int i => new FormulaValue.Number(i),
        string s => new FormulaValue.Text(s),
        bool b => FormulaValue.Bool.Of(b),
        _ => throw new ArgumentException($"Unsupported test value {o}"),
    };

    public TestTable Column(string key, string name, params object?[] values)
    {
        if (values.Length != Rows)
        {
            throw new ArgumentException($"Column {key} has {values.Length} values but the table has {Rows} rows.");
        }

        _names.Add(new(key, name));
        _base[key] = values.Select(V).ToArray();
        return this;
    }

    public TestTable Formula(string key, string name, string text)
    {
        _names.Add(new(key, name));
        _formulaText[key] = text;
        return this;
    }

    public CompiledFormula Compile(string text)
    {
        var result = FormulaCompiler.Compile(text, Resolver);
        Assert.True(result.Success, $"Compile of '{text}' failed: {result.Error?.Code} {result.Error?.Message}");
        return result.Formula!;
    }

    public FormulaSheet Sheet(IReadOnlyDictionary<string, CompiledFormula>? extra = null, params string[] dropBaseColumns)
    {
        var formulas = _formulaText.ToDictionary(kv => kv.Key, kv => Compile(kv.Value), StringComparer.Ordinal);
        if (extra is not null)
        {
            foreach (var (k, f) in extra)
            {
                formulas[k] = f;
            }
        }

        var bases = new Dictionary<string, Func<int, FormulaValue>>(StringComparer.Ordinal);
        foreach (var (key, values) in _base)
        {
            if (dropBaseColumns.Contains(key))
            {
                continue;
            }

            var k = key;
            var v = values;
            bases[k] = row =>
            {
                BaseCalls[k] = BaseCalls.GetValueOrDefault(k) + 1;
                return v[row];
            };
        }

        return new FormulaSheet(Rows, bases, formulas);
    }

    public FormulaSheet SheetWithProbe(string text) =>
        Sheet(new Dictionary<string, CompiledFormula> { [ProbeKey] = Compile(text) });

    public FormulaValue Eval(string text, int row = 0) => SheetWithProbe(text).Get(ProbeKey, row);

    public FormulaValue[] EvalAll(string text)
    {
        var sheet = SheetWithProbe(text);
        return Enumerable.Range(0, Rows).Select(r => sheet.Get(ProbeKey, r)).ToArray();
    }

    /// <summary>Evaluates a formula that needs no columns.</summary>
    public static FormulaValue Scalar(string text) => new TestTable(1).Eval(text);
}

internal static class Expect
{
    public static decimal D(string s) => decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture);

    public static void Number(decimal expected, FormulaValue actual)
    {
        var n = Assert.IsType<FormulaValue.Number>(actual);
        Assert.Equal(expected, n.Value);
    }

    public static void Number(string expected, FormulaValue actual) => Number(D(expected), actual);

    public static void Text(string expected, FormulaValue actual)
    {
        var t = Assert.IsType<FormulaValue.Text>(actual);
        Assert.Equal(expected, t.Value);
    }

    public static void Bool(bool expected, FormulaValue actual)
    {
        var b = Assert.IsType<FormulaValue.Bool>(actual);
        Assert.Equal(expected, b.Value);
    }

    public static FormulaValue.Error Error(string code, FormulaValue actual)
    {
        var e = Assert.IsType<FormulaValue.Error>(actual);
        Assert.Equal(code, e.Code);
        Assert.False(string.IsNullOrWhiteSpace(e.Message));
        return e;
    }

    public static FormulaValue.Error CompileError(string code, string text, Func<string, string?>? resolver = null)
    {
        var result = FormulaCompiler.Compile(text, resolver ?? (_ => null));
        Assert.False(result.Success, $"Expected '{text}' to fail with {code} but it compiled to '{result.Formula?.Canonical}'.");
        Assert.Null(result.Formula);
        Assert.NotNull(result.Error);
        Assert.Equal(code, result.Error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        return result.Error;
    }
}
