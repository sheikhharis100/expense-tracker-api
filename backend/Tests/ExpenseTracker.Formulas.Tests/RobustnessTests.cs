using System.Text;
using static ExpenseTracker.Formulas.Tests.TestTable;

namespace ExpenseTracker.Formulas.Tests;

public class RobustnessTests
{
    private static readonly HashSet<string> KnownCodes = new()
    {
        FormulaErrors.DivideByZero, FormulaErrors.Value, FormulaErrors.Ref, FormulaErrors.Name,
        FormulaErrors.Circular, FormulaErrors.Num, FormulaErrors.Syntax,
    };

    [Theory]
    [InlineData("(((")]
    [InlineData(")))")]
    [InlineData("[[")]
    [InlineData("]]")]
    [InlineData("\"")]
    [InlineData("SUM(")]
    [InlineData("IF(,,)")]
    [InlineData("1++")]
    [InlineData("[#]")]
    [InlineData("[@]")]
    [InlineData("=\0")]
    [InlineData("1e5")]
    [InlineData("\U0001F600")]
    [InlineData("&&")]
    [InlineData("<>")]
    [InlineData(">=1")]
    [InlineData("TRUE(")]
    [InlineData("@")]
    [InlineData("#")]
    [InlineData(",")]
    [InlineData("SUM([Amount]")]
    [InlineData("SUMIF(")]
    [InlineData("SUMIFS([Amount],,)")]
    [InlineData("ROUND(1,,2)")]
    [InlineData("'Amount'")]
    [InlineData("$A$1")]
    [InlineData("A1:B2")]
    [InlineData("{1,2}")]
    [InlineData("1 +\n")]
    [InlineData("eval(\"1\")")]
    [InlineData("System.IO.File.Delete(\"x\")")]
    [InlineData("[Amount]()")]
    [InlineData("((1)")]
    [InlineData("SUM((1)")]
    public void Malformed_input_returns_an_error_result_instead_of_throwing(string formula)
    {
        var result = FormulaCompiler.Compile(formula, Ledger().Resolver);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains(result.Error!.Code, KnownCodes);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
    }

    private static readonly string[] Pieces =
    {
        "[Amount]", "[@Amount]", "[#c2]", "[Tip %]", "[Qty]", "[Category]", "[Date]", "[#c99]", "[Nope]",
        "SUM(", "AVERAGE(", "MIN(", "MAX(", "COUNT(", "IF(", "ROUND(", "ABS(", "SUMIF(", "SUMIFS(", "FOO(",
        "(", ")", "(", ")", ",", ",", "\"", "\"Food\"", "\">100\"", "\"<>\"", "\"\"\"\"",
        "1", "0", "2.5", ".5", "100", "79228162514264337593543950335",
        "+", "-", "*", "/", "&", "=", "<>", "<", ">", "<=", ">=", " ", " ",
        "TRUE", "false", "foo", "[", "]", "#", "@", "%", "^", ";", ":", "\0", "é", "\n", "=",
    };

    [Fact]
    public void Fuzzed_token_soup_never_throws_and_compiled_formulas_round_trip()
    {
        var random = new Random(20261105);
        var ledger = Ledger();
        var compiled = 0;

        for (var i = 0; i < 4000; i++)
        {
            var sb = new StringBuilder();
            var count = random.Next(1, 16);
            for (var j = 0; j < count; j++)
            {
                sb.Append(Pieces[random.Next(Pieces.Length)]);
            }

            var text = sb.ToString();
            var result = FormulaCompiler.Compile(text, ledger.Resolver);
            Assert.True(result.Success ^ (result.Error is not null), text);

            if (!result.Success)
            {
                Assert.Contains(result.Error!.Code, KnownCodes);
                continue;
            }

            compiled++;
            var formula = result.Formula!;
            var sheet = ledger.Sheet(new Dictionary<string, CompiledFormula> { ["probe"] = formula });
            var values = Enumerable.Range(0, ledger.Rows).Select(r => sheet.Get("probe", r)).ToArray();
            Assert.All(values, v => Assert.NotNull(v));

            // Canonical text is stable, and its display form compiles back to the same canonical text.
            var again = FormulaCompiler.Compile(formula.Canonical, ledger.Resolver);
            Assert.True(again.Success, $"'{text}' -> '{formula.Canonical}': {again.Error?.Message}");
            Assert.Equal(formula.Canonical, again.Formula!.Canonical);

            var display = FormulaCompiler.ToDisplay(formula.Canonical, ledger.NameOf);
            var fromDisplay = FormulaCompiler.Compile(display, ledger.Resolver);
            Assert.True(fromDisplay.Success, $"'{display}': {fromDisplay.Error?.Message}");
            Assert.Equal(formula.Canonical, fromDisplay.Formula!.Canonical);

            var sheet2 = ledger.Sheet(new Dictionary<string, CompiledFormula> { ["probe"] = again.Formula });
            Assert.Equal(values, Enumerable.Range(0, ledger.Rows).Select(r => sheet2.Get("probe", r)).ToArray());
        }

        Assert.True(compiled > 50, $"Only {compiled} fuzzed formulas compiled; the fuzzer is not exercising evaluation.");
    }

    [Fact]
    public void Random_characters_never_throw()
    {
        var random = new Random(42);
        var resolver = Ledger().Resolver;
        for (var i = 0; i < 3000; i++)
        {
            var chars = new char[random.Next(0, 60)];
            for (var j = 0; j < chars.Length; j++)
            {
                chars[j] = random.Next(4) == 0 ? (char)random.Next(0, 0xFFFF) : (char)random.Next(32, 127);
            }

            var text = new string(chars);
            var result = FormulaCompiler.Compile(text, resolver);
            Assert.True(result.Success ^ (result.Error is not null));
            _ = FormulaCompiler.ToDisplay(text, key => key);
        }
    }

    [Fact]
    public void Mutated_valid_formulas_never_throw()
    {
        var random = new Random(7);
        var ledger = Ledger();
        string[] seeds =
        {
            "IF([Qty]=0, 0, [Amount]/[Qty])",
            "SUMIFS([Amount], [Category], \"Food\", [Date], \">=2026-11-01\")",
            "ROUND([Amount] * [Tip %] / 100, 2) & \" tip\"",
            "[Amount] / SUM([Amount])",
        };

        for (var i = 0; i < 3000; i++)
        {
            var chars = seeds[random.Next(seeds.Length)].ToList();
            var edits = random.Next(1, 4);
            for (var e = 0; e < edits && chars.Count > 0; e++)
            {
                var at = random.Next(chars.Count);
                switch (random.Next(3))
                {
                    case 0:
                        chars.RemoveAt(at);
                        break;
                    case 1:
                        const string inserts = "()[]\",=<>&+-*/@#";
                        chars.Insert(at, inserts[random.Next(inserts.Length)]);
                        break;
                    default:
                        chars[at] = (char)random.Next(32, 127);
                        break;
                }
            }

            var result = FormulaCompiler.Compile(new string(chars.ToArray()), ledger.Resolver);
            if (result.Success)
            {
                var sheet = ledger.Sheet(new Dictionary<string, CompiledFormula> { ["probe"] = result.Formula! });
                for (var row = 0; row < ledger.Rows; row++)
                {
                    Assert.NotNull(sheet.Get("probe", row));
                }
            }
            else
            {
                Assert.Contains(result.Error!.Code, KnownCodes);
            }
        }
    }

    [Fact]
    public void Huge_text_results_are_capped()
    {
        // Each column doubles the previous one's text: without a cap this would need 2^40 characters.
        var formulas = new Dictionary<string, CompiledFormula>
        {
            ["t0"] = FormulaCompiler.Compile("\"xxxxxxxxxxxxxxxx\"", _ => null).Formula!,
        };
        for (var i = 1; i <= 40; i++)
        {
            formulas["t" + i] = FormulaCompiler.Compile($"[#t{i - 1}] & [#t{i - 1}]", _ => null).Formula!;
        }

        var sheet = new FormulaSheet(1, new Dictionary<string, Func<int, FormulaValue>>(), formulas);
        Expect.Error(FormulaErrors.Value, sheet.Get("t40", 0));
        Assert.Equal(16 * 1024, Assert.IsType<FormulaValue.Text>(sheet.Get("t10", 0)).Value.Length);
    }
}

public class FormulaValueTests
{
    [Theory]
    [InlineData("3.00", "3")]
    [InlineData("0.1", "0.1")]
    [InlineData("-1.50", "-1.5")]
    [InlineData("1000000", "1000000")]
    [InlineData("0.000", "0")]
    [InlineData("120", "120")]
    public void Numbers_display_without_trailing_zeros(string value, string display) =>
        Assert.Equal(display, new FormulaValue.Number(Expect.D(value)).AsDisplayString());

    [Fact]
    public void Other_values_display()
    {
        Assert.Equal("TRUE", FormulaValue.Bool.True.AsDisplayString());
        Assert.Equal("FALSE", FormulaValue.Bool.False.AsDisplayString());
        Assert.Equal("", FormulaValue.Blank.Instance.AsDisplayString());
        Assert.Equal("hi", new FormulaValue.Text("hi").AsDisplayString());
        Assert.Equal("#DIV/0!", new FormulaValue.Error(FormulaErrors.DivideByZero, "x").AsDisplayString());
        Assert.Equal("0.3333333333333333333333333333", Scalar("1/3").AsDisplayString());
    }

    [Fact]
    public void Factory_helpers_map_null_and_empty_to_blank()
    {
        Assert.IsType<FormulaValue.Blank>(FormulaValue.FromText(null));
        Assert.IsType<FormulaValue.Blank>(FormulaValue.FromText(""));
        Assert.IsType<FormulaValue.Blank>(FormulaValue.FromNumber(null));
        Assert.IsType<FormulaValue.Blank>(FormulaValue.FromBool(null));
        Expect.Text(" ", FormulaValue.FromText(" "));
        Expect.Number("2.5", FormulaValue.FromNumber(2.5m));
        Assert.True(new FormulaValue.Error("#VALUE!", "m").IsError);
        Assert.True(FormulaValue.Blank.Instance.IsBlank);
        Assert.False(new FormulaValue.Number(0m).IsBlank);
    }
}
