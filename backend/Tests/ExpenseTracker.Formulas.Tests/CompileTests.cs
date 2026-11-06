using static ExpenseTracker.Formulas.Tests.TestTable;

namespace ExpenseTracker.Formulas.Tests;

public class CompileErrorTests
{
    [Theory]
    [InlineData("FOO(1)", "FOO")]
    [InlineData("vlookup([Amount], 1)", "VLOOKUP")]
    [InlineData("STDEV.S([Amount])", "STDEV.S")]
    [InlineData("1 + NOW()", "NOW")]
    public void Unknown_function_is_name_error(string formula, string name)
    {
        var error = Expect.CompileError(FormulaErrors.Name, formula, Ledger().Resolver);
        Assert.Contains(name, error.Message);
    }

    [Fact]
    public void Function_names_are_case_insensitive()
    {
        Expect.Number("3", Scalar("sum(1,2)"));
        Expect.Number("3", Scalar("Sum (1, 2)"));
        Assert.Equal("SUM(1, 2)", FormulaCompiler.Compile("sUm(1,2)", _ => null).Formula!.Canonical);
    }

    [Fact]
    public void Unknown_display_name_is_name_error()
    {
        var error = Expect.CompileError(FormulaErrors.Name, "[Amount] + [Nope]", Ledger().Resolver);
        Assert.Contains("[Nope]", error.Message);
        Assert.Contains("position 12", error.Message);
    }

    [Fact]
    public void Bare_word_suggests_brackets()
    {
        var error = Expect.CompileError(FormulaErrors.Name, "Amount * 2", Ledger().Resolver);
        Assert.Contains("[Amount]", error.Message);
    }

    [Theory]
    [InlineData("(1+2", "Expected ')' at position 5")]
    [InlineData("=SUM([Amount]", "Expected ')' at position 14")]
    [InlineData("SUM(1 2)", "Expected ',' or ')' at position 7")]
    [InlineData("1+", "Expected a value after '+' at position 3")]
    [InlineData("2*(3+)", "Expected a value after '+' at position 6")]
    [InlineData("*1", "Expected a value at position 1")]
    [InlineData("SUM(1,)", "Expected a value after ',' at position 7")]
    [InlineData("1 = = 2", "position 5")]
    [InlineData("\"abc", "position 1")]
    [InlineData("[Amount] & \"unterminated", "position 12")]
    [InlineData("1 2", "Unexpected '2' at position 3")]
    [InlineData("[Amount] [Qty]", "Unexpected '[Qty]' at position 10")]
    [InlineData(")", "at position 1, but found ')'")]
    [InlineData("1+2)", "Unexpected ')' at position 4")]
    [InlineData("", "empty")]
    [InlineData("=", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("[Amount", "Missing ']'")]
    [InlineData("[]", "Empty column reference at position 1")]
    [InlineData("[@]", "Empty column reference")]
    [InlineData("[#]", "Missing column key")]
    [InlineData("[#c 1]", "not a valid column key")]
    [InlineData("[a[b]]", "Unexpected '['")]
    [InlineData("1]", "Unexpected ']' at position 2")]
    [InlineData("2^3", "'^'")]
    [InlineData("50%", "'%'")]
    [InlineData("1;2", "';'")]
    [InlineData("1.2.3", "position 4")]
    [InlineData("IF(1)", "IF at position 1 takes 2 or 3 arguments")]
    [InlineData("1+ROUND(1)", "ROUND at position 3 takes exactly 2 arguments")]
    [InlineData("ABS()", "ABS")]
    [InlineData("SUM()", "SUM")]
    [InlineData("IF(1,2,3,4)", "IF")]
    public void Syntax_errors_have_helpful_messages_with_positions(string formula, string expectedFragment)
    {
        var error = Expect.CompileError(FormulaErrors.Syntax, formula, Ledger().Resolver);
        Assert.Contains(expectedFragment, error.Message);
    }

    [Fact]
    public void Unterminated_string_message_mentions_the_quote()
    {
        var error = Expect.CompileError(FormulaErrors.Syntax, "\"abc");
        Assert.Contains("closing quote", error.Message);
    }

    [Fact]
    public void Null_formula_is_an_error_not_an_exception() =>
        Expect.CompileError(FormulaErrors.Syntax, null!);

    [Fact]
    public void Formula_length_limit()
    {
        // 1 + "+1" * 499 = 999 characters, plus one space = exactly 1000.
        var atLimit = "1" + string.Concat(Enumerable.Repeat("+1", 499)) + " ";
        Assert.Equal(FormulaCompiler.MaxLength, atLimit.Length);
        Expect.Number("500", Scalar(atLimit));

        var error = Expect.CompileError(FormulaErrors.Syntax, atLimit + "1");
        Assert.Contains("1000", error.Message);
        Assert.Contains("too long", error.Message);
    }

    [Fact]
    public void Nesting_depth_limit_for_parentheses()
    {
        var ok = new string('(', 64) + "1" + new string(')', 64);
        Expect.Number("1", Scalar(ok));

        var tooDeep = new string('(', 65) + "1" + new string(')', 65);
        var error = Expect.CompileError(FormulaErrors.Syntax, tooDeep);
        Assert.Contains("nested too deeply", error.Message);
        Assert.Contains("64", error.Message);
    }

    [Fact]
    public void Nesting_depth_limit_for_functions_and_signs()
    {
        string NestedAbs(int n) => string.Concat(Enumerable.Repeat("ABS(", n)) + "-1" + new string(')', n);

        Expect.Number("1", Scalar(NestedAbs(63))); // 63 calls + 1 unary sign = 64 levels
        Expect.CompileError(FormulaErrors.Syntax, NestedAbs(64));
        Expect.CompileError(FormulaErrors.Syntax, new string('-', 100) + "1");
        Expect.Number("1", Scalar(new string('-', 64) + "1"));
    }

    [Fact]
    public void Long_flat_expressions_are_not_limited_by_depth()
    {
        var flat = string.Join("+", Enumerable.Repeat("(1)", 200));
        Expect.Number("200", Scalar(flat));
    }
}

public class ReferenceTests
{
    [Fact]
    public void Deleted_or_unknown_key_compiles_but_evaluates_to_ref_error()
    {
        var ledger = Ledger();
        var compiled = ledger.Compile("[#c99] * 2");
        Assert.Equal("[#c99] * 2", compiled.Canonical);
        Assert.Contains("c99", compiled.References);

        Expect.Error(FormulaErrors.Ref, ledger.Eval("[#c99] * 2"));
        Expect.Error(FormulaErrors.Ref, ledger.Eval("SUM([#c99])"));
        Expect.Error(FormulaErrors.Ref, ledger.Eval("SUMIF([#c99], \">1\", [Amount])"));
        Expect.Error(FormulaErrors.Ref, ledger.Eval("SUMIF([Amount], \">1\", [#c99])"));
        Expect.Error(FormulaErrors.Ref, ledger.Eval("IF(TRUE, [@#c99], 0)"));
    }

    [Fact]
    public void Column_deleted_after_the_formula_was_saved_gives_ref_error()
    {
        var ledger = Ledger();
        var formula = ledger.Compile("[Amount] * [Tip %] / 100"); // compiled while "Tip %" (c2) existed
        var sheet = ledger.Sheet(new Dictionary<string, CompiledFormula> { ["f"] = formula }, dropBaseColumns: new[] { "c2" });

        var error = Expect.Error(FormulaErrors.Ref, sheet.Get("f", 0));
        Assert.Contains("c2", error.Message);
    }

    [Fact]
    public void Display_names_are_case_insensitive_and_trimmed()
    {
        var ledger = Ledger();
        Assert.Equal("[#c2]", ledger.Compile("[ tip % ]").Canonical);
        Assert.Equal("[#amount]", ledger.Compile("[AMOUNT]").Canonical);
        Assert.Equal("[@#amount]", ledger.Compile("[@ amount ]").Canonical);
        Assert.Equal("[@#c2]", ledger.Compile("[@#c2]").Canonical);
    }

    [Fact]
    public void Key_references_do_not_consult_the_name_resolver()
    {
        var result = FormulaCompiler.Compile("[#amount] + [@#c12]", name => throw new InvalidOperationException("should not resolve " + name));
        Assert.True(result.Success);
        Assert.Equal("[#amount] + [@#c12]", result.Formula!.Canonical);
        Assert.Equal(new[] { "amount", "c12" }, result.Formula.References.OrderBy(k => k));
    }

    [Fact]
    public void Name_resolver_is_case_insensitive_and_rejects_ambiguous_names()
    {
        var resolve = FormulaCompiler.CreateNameResolver(new Dictionary<string, string>
        {
            ["amount"] = "Amount",
            ["c1"] = "Tip",
            ["c2"] = " tip ",
            ["c3"] = "Notes",
        });

        Assert.Equal("amount", resolve("aMoUnT"));
        Assert.Equal("c3", resolve("  notes "));
        Assert.Null(resolve("Tip"));     // ambiguous
        Assert.Null(resolve("Missing"));
    }
}

public class CanonicalFormTests
{
    [Fact]
    public void Compile_rewrites_display_names_to_keys()
    {
        var compiled = Ledger().Compile("=[Amount]*[Tip %]/100");
        Assert.Equal("[#amount] * [#c2] / 100", compiled.Canonical);
        Assert.Equal(new[] { "amount", "c2" }, compiled.References.OrderBy(k => k));
    }

    [Theory]
    [InlineData("[@Amount]+SUM([amount])", "[@#amount] + SUM([#amount])")]
    [InlineData("if(true,\"a\"\"b\",1.50)", "IF(TRUE, \"a\"\"b\", 1.50)")]
    [InlineData("  -( [Qty] )*.5 ", "-([#c1]) * 0.5")]
    [InlineData("SUMIF([Category],\"<>Food\",[Amount])", "SUMIF([#category], \"<>Food\", [#amount])")]
    [InlineData("1--1", "1 - -1")]
    [InlineData("[Date]>=\"2026-11-01\"", "[#date] >= \"2026-11-01\"")]
    public void Canonical_text_is_normalised(string formula, string canonical) =>
        Assert.Equal(canonical, Ledger().Compile(formula).Canonical);

    [Theory]
    [InlineData("=[Amount]*[Tip %]/100")]
    [InlineData("IF([Qty]=0, 0, [Amount]/[Qty])")]
    [InlineData("[Amount]/SUM([Amount])")]
    [InlineData("SUMIFS([Amount],[Category],\"Food\",[Date],\">=2026-11-01\")")]
    [InlineData("\"He said \"\"[#amount]\"\"\" & [Description]")]
    [InlineData("-(-(1+2)*3)&\"x\"")]
    public void Compiling_the_canonical_text_again_gives_the_same_text_and_results(string formula)
    {
        var ledger = Ledger();
        var first = ledger.Compile(formula);
        var second = ledger.Compile(first.Canonical);
        Assert.Equal(first.Canonical, second.Canonical);

        var display = FormulaCompiler.ToDisplay(first.Canonical, ledger.NameOf);
        var third = ledger.Compile(display);
        Assert.Equal(first.Canonical, third.Canonical);

        Assert.Equal(ledger.EvalAll(formula), ledger.EvalAll(first.Canonical));
    }

    [Fact]
    public void ToDisplay_maps_keys_back_to_names()
    {
        var ledger = Ledger();
        var canonical = ledger.Compile("[Amount]*[Tip %]/100 + [@Qty]").Canonical;
        Assert.Equal("[Amount] * [Tip %] / 100 + [@Qty]", FormulaCompiler.ToDisplay(canonical, ledger.NameOf));
    }

    [Fact]
    public void Renaming_a_column_changes_the_display_but_not_the_formula_or_results()
    {
        var before = Ledger();
        var formula = before.Compile("[Amount] * [Tip %] / 100");

        // The user renames "Tip %" to "Gratuity %" (key c2 is unchanged) and reorders columns.
        var after = new TestTable(5)
            .Column("c2", "Gratuity %", 10m, null, 15m, "n/a", 5m)
            .Column("amount", "Amount", 50m, 150.25m, 200m, 20m, null);

        Assert.Equal("[Amount] * [Gratuity %] / 100", FormulaCompiler.ToDisplay(formula.Canonical, after.NameOf));
        Assert.Equal("[Amount] * [Tip %] / 100", FormulaCompiler.ToDisplay(formula.Canonical, before.NameOf));

        var resaved = after.Compile(FormulaCompiler.ToDisplay(formula.Canonical, after.NameOf));
        Assert.Equal(formula.Canonical, resaved.Canonical);

        var oldSheet = before.Sheet(new Dictionary<string, CompiledFormula> { ["f"] = formula });
        var newSheet = after.Sheet(new Dictionary<string, CompiledFormula> { ["f"] = formula });
        for (var row = 0; row < 5; row++)
        {
            Assert.Equal(oldSheet.Get("f", row), newSheet.Get("f", row));
        }

        Expect.Number("5", newSheet.Get("f", 0));
    }

    [Fact]
    public void ToDisplay_keeps_unknown_keys_and_string_literals()
    {
        Func<string, string?> names = key => key == "amount" ? "Amount" : null;
        Assert.Equal("[#c99] + 1", FormulaCompiler.ToDisplay("[#c99] + 1", names));
        Assert.Equal("\"[#amount]\" & [Amount]", FormulaCompiler.ToDisplay("\"[#amount]\" & [#amount]", names));
        Assert.Equal("[@#c99] & [@Amount]", FormulaCompiler.ToDisplay("[@#c99] & [@#amount]", names));
    }

    [Fact]
    public void ToDisplay_falls_back_to_keys_for_names_that_cannot_round_trip()
    {
        var names = new Dictionary<string, string>
        {
            ["c1"] = "a]b",
            ["c2"] = "#hash",
            ["c3"] = "@at",
            ["c4"] = "  ",
            ["c5"] = "Tip",
            ["c6"] = "tip",
        };
        string? KeyToName(string key) => names.GetValueOrDefault(key);

        Assert.Equal("[#c1] + [#c2] + [#c3] + [#c4]", FormulaCompiler.ToDisplay("[#c1] + [#c2] + [#c3] + [#c4]", KeyToName));

        // Without a resolver the duplicate names are shown; with one, ambiguous names stay as keys.
        Assert.Equal("[Tip] + [tip]", FormulaCompiler.ToDisplay("[#c5] + [#c6]", KeyToName));
        var resolver = FormulaCompiler.CreateNameResolver(names);
        Assert.Equal("[#c5] + [#c6]", FormulaCompiler.ToDisplay("[#c5] + [#c6]", KeyToName, resolver));
    }

    [Theory]
    [InlineData("[#amount")]
    [InlineData("\"unterminated [#amount]")]
    [InlineData("[[#amount]]")]
    [InlineData("]")]
    [InlineData("")]
    [InlineData("[#]")]
    [InlineData("[@]")]
    public void ToDisplay_never_throws_on_malformed_text(string text)
    {
        var display = FormulaCompiler.ToDisplay(text, key => key == "amount" ? "Amount" : null);
        Assert.NotNull(display);
    }
}
