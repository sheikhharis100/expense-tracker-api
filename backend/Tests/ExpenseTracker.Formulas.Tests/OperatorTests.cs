using static ExpenseTracker.Formulas.Tests.TestTable;

namespace ExpenseTracker.Formulas.Tests;

public class OperatorTests
{
    [Theory]
    [InlineData("1+2*3", "7")]
    [InlineData("(1+2)*3", "9")]
    [InlineData("10-4-3", "3")]
    [InlineData("100/10/2", "5")]
    [InlineData("2*3+4*5", "26")]
    [InlineData("2+3*4-6/2", "11")]
    [InlineData("2*(3+4)*5", "70")]
    [InlineData("((2))", "2")]
    [InlineData("=1+1", "2")]
    [InlineData("  = 3 * 4 ", "12")]
    [InlineData("12.5", "12.5")]
    [InlineData(".5*4", "2")]
    [InlineData("12.", "12")]
    public void Arithmetic_follows_precedence_and_parentheses(string formula, string expected) =>
        Expect.Number(expected, Scalar(formula));

    [Theory]
    [InlineData("-2*3", "-6")]
    [InlineData("--5", "5")]
    [InlineData("-(2+3)", "-5")]
    [InlineData("+5", "5")]
    [InlineData("2*-3", "-6")]
    [InlineData("1--1", "2")]
    [InlineData("-3+5", "2")]
    [InlineData("-\"4\"", "-4")]
    [InlineData("-TRUE", "-1")]
    public void Unary_minus_and_plus(string formula, string expected) =>
        Expect.Number(expected, Scalar(formula));

    [Fact]
    public void Decimal_arithmetic_is_exact()
    {
        var sum = Assert.IsType<FormulaValue.Number>(Scalar("0.1+0.2"));
        Assert.True(sum.Value == 0.3m);
        Assert.Equal("0.3", sum.AsDisplayString());
        Expect.Bool(true, Scalar("0.1+0.2=0.3"));
        Expect.Number("115", Scalar("100*1.15"));
        Expect.Number("1.21", Scalar("1.1*1.1"));
        Expect.Number("2.5", Scalar("10/4"));
    }

    [Theory]
    [InlineData("\"Hello\" & \" \" & \"World\"", "Hello World")]
    [InlineData("\"Say \"\"hi\"\"\"", "Say \"hi\"")]
    [InlineData("\"\"\"\"", "\"")]
    [InlineData("\"\"", "")]
    [InlineData("1&2", "12")]
    [InlineData("\"Total: \"&1.50*2", "Total: 3")]
    [InlineData("1+2&3", "33")]
    [InlineData("\"x\"&TRUE", "xTRUE")]
    [InlineData("\"ratio \"&1/4", "ratio 0.25")]
    public void Text_literals_escapes_and_concatenation(string formula, string expected) =>
        Expect.Text(expected, Scalar(formula));

    [Fact]
    public void Concatenating_a_blank_cell_uses_empty_text()
    {
        var ledger = Ledger();
        Expect.Text("!", ledger.Eval("[Category]&\"!\"", row: 4));
        Expect.Text("Food/Lunch", ledger.Eval("[Category]&\"/\"&[Description]", row: 0));
    }

    [Theory]
    [InlineData("1<2", true)]
    [InlineData("2<=2", true)]
    [InlineData("3<>3", false)]
    [InlineData("3>=4", false)]
    [InlineData("5>4", true)]
    [InlineData("1=1", true)]
    [InlineData("1+1=2", true)]
    [InlineData("\"abc\"=\"ABC\"", true)]
    [InlineData("\"abc\"<>\"abd\"", true)]
    [InlineData("\"apple\"<\"Banana\"", true)]
    [InlineData("\"2026-11-05\">\"2026-11-01\"", true)]
    [InlineData("\"2026-11-05\"<=\"2026-10-31\"", false)]
    [InlineData("\"10\"=10", true)]
    [InlineData("\"9\"<10", true)]
    [InlineData("TRUE=TRUE", true)]
    [InlineData("FALSE<TRUE", true)]
    [InlineData("\"a\"&\"b\"=\"AB\"", true)]
    [InlineData("\"a\"&1=\"a1\"", true)]
    public void Comparisons_return_booleans(string formula, bool expected) =>
        Expect.Bool(expected, Scalar(formula));

    [Fact]
    public void Blank_compares_as_zero_with_numbers_and_empty_text_with_text()
    {
        var ledger = Ledger();
        Expect.Bool(true, ledger.Eval("[Amount]=0", row: 4));
        Expect.Bool(true, ledger.Eval("[Amount]<1", row: 4));
        Expect.Bool(true, ledger.Eval("[Amount]=\"\"", row: 4));
        Expect.Bool(true, ledger.Eval("[Category]=\"\"", row: 4));
        Expect.Bool(false, ledger.Eval("[Category]=\"Food\"", row: 4));
    }

    [Theory]
    [InlineData("TRUE+1", "2")]
    [InlineData("true*3", "3")]
    [InlineData("FALSE*5", "0")]
    [InlineData("\" 7 \"+1", "8")]
    [InlineData("\"5\"*2", "10")]
    public void Booleans_and_numeric_text_are_converted_in_arithmetic(string formula, string expected) =>
        Expect.Number(expected, Scalar(formula));

    [Theory]
    [InlineData("1/0")]
    [InlineData("0/0")]
    [InlineData("5/(2-2)")]
    public void Division_by_zero(string formula) =>
        Expect.Error(FormulaErrors.DivideByZero, Scalar(formula));

    [Fact]
    public void Dividing_by_a_zero_or_blank_cell_is_div0()
    {
        var ledger = Ledger();
        Expect.Error(FormulaErrors.DivideByZero, ledger.Eval("[Amount]/[Qty]", row: 2)); // Qty = 0
        Expect.Error(FormulaErrors.DivideByZero, ledger.Eval("[Amount]/[Qty]", row: 3)); // Qty blank
        Expect.Number("25", ledger.Eval("[Amount]/[Qty]", row: 0));
    }

    [Theory]
    [InlineData("\"abc\"*2")]
    [InlineData("\"abc\"+1")]
    [InlineData("-\"abc\"")]
    [InlineData("\"1,000\"+1")]
    [InlineData("\"\"+1")]
    public void Non_numeric_text_in_arithmetic_is_value_error(string formula) =>
        Expect.Error(FormulaErrors.Value, Scalar(formula));

    [Fact]
    public void Text_column_in_arithmetic_is_value_error()
    {
        var ledger = Ledger();
        Expect.Error(FormulaErrors.Value, ledger.Eval("[Description]+1", row: 0));
        Expect.Error(FormulaErrors.Value, ledger.Eval("[Amount]*[Tip %]/100", row: 3)); // Tip % = "n/a"
        Expect.Number("5", ledger.Eval("[Amount]*[Tip %]/100", row: 0));
    }

    [Theory]
    [InlineData("79228162514264337593543950335+1")]
    [InlineData("79228162514264337593543950335*2")]
    [InlineData("-79228162514264337593543950335-1")]
    [InlineData("79228162514264337593543950335/0.1")]
    public void Decimal_overflow_is_num_error(string formula) =>
        Expect.Error(FormulaErrors.Num, Scalar(formula));

    [Fact]
    public void Number_literal_too_large_for_decimal_is_num_compile_error() =>
        Expect.CompileError(FormulaErrors.Num, "99999999999999999999999999999999+1");

    [Theory]
    [InlineData("1/0+1", FormulaErrors.DivideByZero)]
    [InlineData("\"a\"&(1/0)", FormulaErrors.DivideByZero)]
    [InlineData("(1/0)=1", FormulaErrors.DivideByZero)]
    [InlineData("-(1/0)", FormulaErrors.DivideByZero)]
    [InlineData("\"abc\"*2+1/0", FormulaErrors.Value)]
    [InlineData("1/0+\"abc\"*2", FormulaErrors.DivideByZero)]
    public void Errors_propagate_through_operators(string formula, string code) =>
        Expect.Error(code, Scalar(formula));
}
