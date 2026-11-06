using static ExpenseTracker.Formulas.Tests.TestTable;

namespace ExpenseTracker.Formulas.Tests;

public class AggregateFunctionTests
{
    [Theory]
    [InlineData("SUM([Amount])", "420.25")]
    [InlineData("sum([Amount])", "420.25")]
    [InlineData("SUM([Amount], 5)", "425.25")]
    [InlineData("SUM([Amount], [Amount])", "840.5")]
    [InlineData("SUM(([Amount]))", "420.25")]
    [InlineData("SUM([Tip %])", "30")]          // blank and "n/a" ignored
    [InlineData("SUM([Qty])", "3")]             // text "3" in a column is ignored, like Excel
    [InlineData("SUM([Description])", "0")]
    [InlineData("AVERAGE([Amount])", "105.0625")] // 420.25 / 4 numeric cells
    [InlineData("AVERAGE([Tip %])", "10")]
    [InlineData("MIN([Amount])", "20")]
    [InlineData("MAX([Amount])", "200")]
    [InlineData("MIN([Amount], -5)", "-5")]
    [InlineData("MAX([Amount], 1000)", "1000")]
    [InlineData("MIN([Description])", "0")]     // no numbers -> 0
    [InlineData("MAX([Description])", "0")]
    [InlineData("COUNT([Amount])", "4")]
    [InlineData("COUNT([Category])", "0")]
    [InlineData("COUNT([Qty])", "3")]
    public void Aggregates_over_whole_columns(string formula, string expected)
    {
        var results = Ledger().EvalAll(formula);
        Assert.All(results, r => Expect.Number(expected, r));
    }

    [Theory]
    [InlineData("SUM(1,2,3)", "6")]
    [InlineData("SUM(\"5\", TRUE)", "6")]
    [InlineData("AVERAGE(1,2)", "1.5")]
    [InlineData("MAX(-3,-7)", "-3")]
    [InlineData("MIN(4, 2.5, 9)", "2.5")]
    [InlineData("COUNT(1,\"2\",\"x\",TRUE)", "3")]
    public void Aggregates_over_scalars(string formula, string expected) =>
        Expect.Number(expected, Scalar(formula));

    [Theory]
    [InlineData("SUM(\"abc\")")]
    [InlineData("AVERAGE(1, \"abc\")")]
    [InlineData("MAX(\"abc\")")]
    public void Non_numeric_text_typed_into_an_aggregate_is_value_error(string formula) =>
        Expect.Error(FormulaErrors.Value, Scalar(formula));

    [Fact]
    public void Average_with_no_numbers_is_div0()
    {
        Expect.Error(FormulaErrors.DivideByZero, Ledger().Eval("AVERAGE([Description])"));
        Expect.Error(FormulaErrors.DivideByZero, Ledger().Eval("AVERAGE([Category])"));
    }

    [Fact]
    public void Aggregates_over_a_range_with_no_numbers()
    {
        var blank = new TestTable(3).Column("amount", "Amount", null, null, "text");
        Expect.Number("0", blank.Eval("SUM([Amount])"));
        Expect.Number("0", blank.Eval("COUNT([Amount])"));
        Expect.Number("0", blank.Eval("MIN([Amount])"));
        Expect.Number("0", blank.Eval("MAX([Amount])"));
        Expect.Error(FormulaErrors.DivideByZero, blank.Eval("AVERAGE([Amount])"));
    }

    [Fact]
    public void Current_row_reference_in_an_aggregate_is_a_single_cell()
    {
        var ledger = Ledger();
        Expect.Number("150.25", ledger.Eval("SUM([@Amount])", row: 1));
        Expect.Number("0", ledger.Eval("SUM([@Amount])", row: 4)); // blank skipped
        Expect.Number("1", ledger.Eval("COUNT([@Amount])", row: 1));
    }

    [Fact]
    public void Expression_argument_is_evaluated_for_the_current_row_not_the_column()
    {
        // Only a column reference written directly as the argument means "whole column".
        var ledger = Ledger();
        Expect.Number("100", ledger.Eval("SUM([Amount]*2)", row: 0));
        Expect.Number("400", ledger.Eval("SUM([Amount]*2)", row: 2));
    }
}

public class ScalarFunctionTests
{
    [Theory]
    [InlineData("IF(1>0,\"yes\",\"no\")", "yes")]
    [InlineData("IF(1<0,\"yes\",\"no\")", "no")]
    [InlineData("if(TRUE, \"a\", \"b\")", "a")]
    public void If_chooses_a_branch(string formula, string expected) =>
        Expect.Text(expected, Scalar(formula));

    [Theory]
    [InlineData("IF(\"TRUE\",1,2)", "1")]
    [InlineData("IF(\"false\",1,2)", "2")]
    [InlineData("IF(0,1,2)", "2")]
    [InlineData("IF(-0.5,1,2)", "1")]
    [InlineData("IF(TRUE,1,1/0)", "1")]
    [InlineData("IF(FALSE,1/0,2)", "2")]
    [InlineData("IF(FALSE, \"x\"*1, 3)", "3")]
    public void If_condition_coercion_and_short_circuit(string formula, string expected) =>
        Expect.Number(expected, Scalar(formula));

    [Fact]
    public void If_without_else_returns_false()
    {
        Expect.Bool(false, Scalar("IF(FALSE, 1)"));
        Expect.Number("1", Scalar("IF(TRUE, 1)"));
    }

    [Fact]
    public void If_condition_errors()
    {
        Expect.Error(FormulaErrors.Value, Scalar("IF(\"maybe\",1,2)"));
        Expect.Error(FormulaErrors.DivideByZero, Scalar("IF(1/0,1,2)"));
    }

    [Fact]
    public void If_with_blank_condition_is_false()
    {
        Expect.Number("2", Ledger().Eval("IF([Amount],1,2)", row: 4));
        Expect.Number("1", Ledger().Eval("IF([Amount],1,2)", row: 0));
    }

    [Fact]
    public void If_guard_avoids_division_by_zero_on_every_row()
    {
        var ledger = Ledger();
        var guarded = ledger.EvalAll("IF([Qty]=0, 0, [Amount]/[Qty])");
        Expect.Number("25", guarded[0]);
        Expect.Number("150.25", guarded[1]);
        Expect.Number("0", guarded[2]); // Qty = 0
        Expect.Number("0", guarded[3]); // Qty blank = 0
        Expect.Number("0", guarded[4]); // blank amount / "3"

        Expect.Error(FormulaErrors.DivideByZero, ledger.Eval("[Amount]/[Qty]", row: 2));
    }

    [Theory]
    [InlineData("ROUND(2.5,0)", "3")]
    [InlineData("ROUND(-2.5,0)", "-3")]
    [InlineData("ROUND(0.5,0)", "1")]
    [InlineData("ROUND(1.005,2)", "1.01")]  // exact in decimal (double would give 1)
    [InlineData("ROUND(2.345,2)", "2.35")]
    [InlineData("ROUND(1.25,1)", "1.3")]
    [InlineData("ROUND(3.14159,3)", "3.142")]
    [InlineData("ROUND(1234.5678,-2)", "1200")]
    [InlineData("ROUND(15,-1)", "20")]
    [InlineData("ROUND(-15,-1)", "-20")]
    [InlineData("ROUND(5,-10)", "0")]
    [InlineData("ROUND(123.456,10)", "123.456")]
    [InlineData("ROUND(2.345,\"2\")", "2.35")]
    [InlineData("ROUND(-1.005, 2)", "-1.01")]
    public void Round_uses_midpoint_away_from_zero(string formula, string expected) =>
        Expect.Number(expected, Scalar(formula));

    [Theory]
    [InlineData("ROUND(1.5, 0.5)", FormulaErrors.Value)]
    [InlineData("ROUND(1, 11)", FormulaErrors.Value)]
    [InlineData("ROUND(1, -11)", FormulaErrors.Value)]
    [InlineData("ROUND(\"abc\", 1)", FormulaErrors.Value)]
    [InlineData("ROUND(1, \"two\")", FormulaErrors.Value)]
    [InlineData("ROUND(1/0, 2)", FormulaErrors.DivideByZero)]
    [InlineData("ROUND(79228162514264337593543950335, -1)", FormulaErrors.Num)]
    public void Round_errors(string formula, string code) =>
        Expect.Error(code, Scalar(formula));

    [Theory]
    [InlineData("ABS(-5)", "5")]
    [InlineData("ABS(5)", "5")]
    [InlineData("ABS(-0.25)", "0.25")]
    [InlineData("ABS(\"-3\")", "3")]
    public void Abs_returns_absolute_value(string formula, string expected) =>
        Expect.Number(expected, Scalar(formula));

    [Fact]
    public void Abs_of_column_and_errors()
    {
        Expect.Number("50", Ledger().Eval("ABS(-[Amount])", row: 0));
        Expect.Error(FormulaErrors.Value, Scalar("ABS(\"x\")"));
    }
}

public class ConditionalSumTests
{
    [Theory]
    [InlineData("SUMIF([Amount], \">100\")", "350.25")]
    [InlineData("SUMIF([Amount], 50)", "50")]
    [InlineData("SUMIF([Amount], \"<=50\")", "70")]
    [InlineData("SUMIF([Amount], \"> 100\")", "350.25")]
    [InlineData("SUMIF([Category], \"Food\", [Amount])", "250")]   // matches "Food" and "food"
    [InlineData("SUMIF([Category], \"FOOD\", [Amount])", "250")]
    [InlineData("SUMIF([Category], \"<>Food\", [Amount])", "170.25")] // blank category doesn't match
    [InlineData("SUMIF([Code], \"100\", [Amount])", "250")]         // numeric text
    [InlineData("SUMIF([Code], 100, [Amount])", "250")]
    [InlineData("SUMIF([Code], \">150\", [Amount])", "150.25")]     // "abc" never matches a numeric operand
    [InlineData("SUMIF([Date], \">=2026-11-10\", [Amount])", "220")] // ISO dates compare as text
    [InlineData("SUMIF([Status], \"paid\", [Tip %])", "10")]       // text/blank summed cells are skipped
    [InlineData("SUMIFS([Amount], [Category], \"Food\", [Status], \"paid\")", "50")]
    [InlineData("SUMIFS([Amount], [Date], \">=2026-11-01\", [Date], \"<2026-11-15\")", "400.25")]
    [InlineData("SUMIFS([Amount], [Category], \"Food\")", "250")]
    [InlineData("SUMIFS([Amount], [Status], \"pending\", [Amount], \">100\")", "200")]
    [InlineData("SUMIFS([Amount], [Status], \"paid\", [Category], \"<>Rent\", [Date], \"<2026-11-15\")", "50")]
    public void Conditional_sums(string formula, string expected) =>
        Expect.Number(expected, Ledger().Eval(formula));

    [Fact]
    public void Criteria_can_come_from_the_current_row()
    {
        var totals = Ledger().EvalAll("SUMIF([Category], [Category], [Amount])");
        Expect.Number("250", totals[0]);    // Food
        Expect.Number("150.25", totals[1]); // Rent
        Expect.Number("250", totals[2]);    // food
        Expect.Number("20", totals[3]);     // Transport
        Expect.Number("0", totals[4]);      // blank criteria matches the blank-category row, whose amount is blank
    }

    [Fact]
    public void Blank_cells_match_only_empty_and_equals_criteria()
    {
        var table = new TestTable(4)
            .Column("category", "Category", "Food", null, "", "Rent")
            .Column("amount", "Amount", 1m, 2m, 4m, 8m);

        Expect.Number("6", table.Eval("SUMIF([Category], \"\", [Amount])"));
        Expect.Number("6", table.Eval("SUMIF([Category], \"=\", [Amount])"));
        Expect.Number("9", table.Eval("SUMIF([Category], \"<>\", [Amount])"));
        Expect.Number("12", table.Eval("SUMIF([Category], \"<>Food\", [Amount])")); // the blank row is excluded
        Expect.Number("1", table.Eval("SUMIF([Category], \"Food\", [Amount])"));
    }

    [Fact]
    public void Boolean_criteria_mean_equality()
    {
        var table = new TestTable(3)
            .Column("flag", "Flag", true, false, true)
            .Column("amount", "Amount", 1m, 2m, 4m);
        Expect.Number("5", table.Eval("SUMIF([Flag], TRUE, [Amount])"));
        Expect.Number("2", table.Eval("SUMIF([Flag], FALSE, [Amount])"));
        Expect.Number("5", table.Eval("SUMIF([Flag], \"true\", [Amount])"));
    }

    [Theory]
    [InlineData("SUMIF(5, \">1\")")]
    [InlineData("SUMIF(\"x\", \">1\")")]
    [InlineData("SUMIF([@Amount], \">1\")")]
    [InlineData("SUMIF([Amount], \">1\", [Amount]*2)")]
    [InlineData("SUMIFS([Amount], \"x\", \"Food\")")]
    [InlineData("SUMIFS([Amount]+1, [Category], \"Food\")")]
    public void Range_arguments_must_be_column_references(string formula) =>
        Expect.CompileError(FormulaErrors.Value, formula, Ledger().Resolver);

    [Theory]
    [InlineData("SUMIF([Amount])")]
    [InlineData("SUMIF([Amount], 1, [Amount], 2)")]
    [InlineData("SUMIFS([Amount], [Category])")]
    [InlineData("SUMIFS([Amount], [Category], \"Food\", [Status])")]
    public void Conditional_sum_arity_errors(string formula) =>
        Expect.CompileError(FormulaErrors.Syntax, formula, Ledger().Resolver);
}

public class ErrorPropagationTests
{
    // Ratio = Amount / Qty is #DIV/0! on row 2 (Qty 0) and row 3 (Qty blank).
    private static TestTable WithRatio() => Ledger().Formula("c9", "Ratio", "[Amount]/[Qty]");

    [Theory]
    [InlineData("SUM([Ratio])")]
    [InlineData("AVERAGE([Ratio])")]
    [InlineData("MIN([Ratio])")]
    [InlineData("MAX([Ratio])")]
    [InlineData("COUNT([Ratio])")]
    [InlineData("SUM([Amount], [Ratio])")]
    [InlineData("SUMIF([Ratio], \">0\")")]
    [InlineData("SUMIF([Ratio], \">0\", [Amount])")]
    [InlineData("SUMIF([Category], \"Food\", [Ratio])")]
    [InlineData("SUMIFS([Amount], [Status], \"paid\", [Ratio], \">0\")")]
    [InlineData("ROUND([Ratio], 2) + SUM([Ratio])")]
    public void An_error_anywhere_in_a_range_is_returned_not_ignored(string formula) =>
        Assert.All(WithRatio().EvalAll(formula), r => Expect.Error(FormulaErrors.DivideByZero, r));

    [Fact]
    public void Errors_in_the_sum_column_only_matter_for_matching_rows()
    {
        // Rent is row 1 only, where Ratio = 150.25 / 1.
        Expect.Number("150.25", WithRatio().Eval("SUMIF([Category], \"Rent\", [Ratio])"));
    }

    [Fact]
    public void Error_in_scalar_argument_propagates()
    {
        Expect.Error(FormulaErrors.DivideByZero, Scalar("SUM(1, 1/0)"));
        Expect.Error(FormulaErrors.DivideByZero, Scalar("ABS(1/0)"));
        Expect.Error(FormulaErrors.DivideByZero, Ledger().Eval("SUMIF([Category], 1/0, [Amount])"));
        Expect.Error(FormulaErrors.DivideByZero, Scalar("IF(TRUE, 1/0, 1)"));
    }
}
