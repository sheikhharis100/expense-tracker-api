using static ExpenseTracker.Formulas.Tests.TestTable;

namespace ExpenseTracker.Formulas.Tests;

public class ImplicitIntersectionTests
{
    [Fact]
    public void Plain_reference_in_a_scalar_position_is_the_current_row()
    {
        var results = Ledger().EvalAll("[Amount] * 0.1");
        Expect.Number("5", results[0]);
        Expect.Number("15.025", results[1]);
        Expect.Number("20", results[2]);
        Expect.Number("2", results[3]);
        Expect.Number("0", results[4]); // blank * 0.1 = 0
    }

    [Fact]
    public void Plain_reference_directly_inside_an_aggregate_is_the_whole_column()
    {
        Assert.All(Ledger().EvalAll("SUM([Amount])"), r => Expect.Number("420.25", r));
    }

    [Fact]
    public void Share_of_total_mixes_both()
    {
        var results = Ledger().EvalAll("ROUND([Amount] / SUM([Amount]) * 100, 2)");
        Expect.Number("11.9", results[0]);  // 50 / 420.25
        Expect.Number("35.75", results[1]); // 150.25 / 420.25
        Expect.Number("47.59", results[2]); // 200 / 420.25
    }

    [Fact]
    public void Explicit_current_row_reference_matches_plain_reference_in_scalar_position()
    {
        var ledger = Ledger();
        Assert.Equal(ledger.EvalAll("[Amount] * 2"), ledger.EvalAll("[@Amount] * 2"));
    }

    [Fact]
    public void Reference_to_a_blank_cell_is_blank()
    {
        var value = Ledger().Eval("[Amount]", row: 4);
        Assert.IsType<FormulaValue.Blank>(value);
        Assert.Equal("", value.AsDisplayString());
    }

    [Fact]
    public void Text_and_date_columns_flow_through()
    {
        Expect.Text("2026-11-03", Ledger().Eval("[Date]", row: 1));
        Expect.Bool(true, Ledger().Eval("[Date] >= \"2026-11-03\"", row: 1));
    }
}

public class FormulaColumnTests
{
    private static TestTable WithFormulas() => Ledger()
        .Formula("c10", "Double", "[Amount] * 2")
        .Formula("c11", "Double Plus One", "[Double] + 1")
        .Formula("c12", "Double Total", "SUM([Double])")
        .Formula("c13", "Share", "[Double] / [Double Total]");

    [Fact]
    public void Formula_column_can_depend_on_other_formula_columns_in_any_evaluation_order()
    {
        var sheet = WithFormulas().Sheet();

        // Ask for the dependent columns before their dependencies have been evaluated.
        Expect.Number("101", sheet.Get("c11", 0));
        Expect.Number("840.5", sheet.Get("c12", 3));
        Expect.Number("100", sheet.Get("c10", 0));
        Expect.Number("0.5", ROUNDED(sheet.Get("c13", 2)));
    }

    private static FormulaValue ROUNDED(FormulaValue v) =>
        v is FormulaValue.Number n ? new FormulaValue.Number(Math.Round(n.Value, 1)) : v;

    [Fact]
    public void Base_cells_are_read_once_even_when_aggregated_on_every_row()
    {
        var table = WithFormulas();
        var sheet = table.Sheet();
        for (var row = 0; row < table.Rows; row++)
        {
            sheet.Get("c13", row);
            sheet.Get("c12", row);
            sheet.Get("c11", row);
        }

        Assert.Equal(table.Rows, table.BaseCalls["amount"]);
    }

    [Fact]
    public void Compiled_formula_can_be_shared_between_sheets_with_different_data()
    {
        var formula = Ledger().Compile("SUM([Amount])");
        var small = new TestTable(2).Column("amount", "Amount", 1m, 2m);
        Expect.Number("420.25", Ledger().Sheet(new Dictionary<string, CompiledFormula> { ["f"] = formula }).Get("f", 0));
        Expect.Number("3", small.Sheet(new Dictionary<string, CompiledFormula> { ["f"] = formula }).Get("f", 1));
    }

    [Fact]
    public void Unknown_key_and_out_of_range_rows_are_ref_errors()
    {
        var sheet = WithFormulas().Sheet();
        Expect.Error(FormulaErrors.Ref, sheet.Get("nope", 0));
        Expect.Error(FormulaErrors.Ref, sheet.Get("amount", -1));
        Expect.Error(FormulaErrors.Ref, sheet.Get("amount", 5));
        Expect.Error(FormulaErrors.Ref, sheet.Get("c10", 99));
        Expect.Error(FormulaErrors.Ref, sheet.Get(null!, 0));
        Assert.True(sheet.HasColumn("c10"));
        Assert.True(sheet.HasColumn("amount"));
        Assert.False(sheet.HasColumn("nope"));
    }

    [Fact]
    public void Sheet_rejects_invalid_construction()
    {
        var formula = Ledger().Compile("1");
        var bases = new Dictionary<string, Func<int, FormulaValue>> { ["amount"] = _ => FormulaValue.Blank.Instance };
        Assert.Throws<ArgumentException>(() =>
            new FormulaSheet(1, bases, new Dictionary<string, CompiledFormula> { ["amount"] = formula }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FormulaSheet(-1, bases, new Dictionary<string, CompiledFormula>()));
    }

    [Fact]
    public void Base_column_returning_null_is_blank()
    {
        var bases = new Dictionary<string, Func<int, FormulaValue>> { ["x"] = _ => null! };
        var sheet = new FormulaSheet(1, bases, new Dictionary<string, CompiledFormula>());
        Assert.IsType<FormulaValue.Blank>(sheet.Get("x", 0));
    }
}

public class CycleTests
{
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Refs(params (string Key, string[] Refs)[] items) =>
        items.ToDictionary(i => i.Key, i => (IReadOnlySet<string>)new HashSet<string>(i.Refs));

    [Fact]
    public void Two_column_cycle_is_found()
    {
        var cycle = FormulaDependencies.FindCycle(Refs(("c3", new[] { "c5", "amount" }), ("c5", new[] { "c3" })));
        Assert.Equal(new[] { "c3", "c5", "c3" }, cycle);
    }

    [Fact]
    public void Self_reference_is_a_cycle()
    {
        Assert.Equal(new[] { "c3", "c3" }, FormulaDependencies.FindCycle(Refs(("c3", new[] { "c3" }))));
    }

    [Fact]
    public void Longer_cycle_is_returned_as_a_closed_path()
    {
        var cycle = FormulaDependencies.FindCycle(Refs(
            ("c0", new[] { "c1" }),
            ("c1", new[] { "c2" }),
            ("c2", new[] { "c3" }),
            ("c3", new[] { "c1" })));
        Assert.Equal(new[] { "c1", "c2", "c3", "c1" }, cycle);
    }

    [Fact]
    public void Acyclic_graphs_have_no_cycle()
    {
        // Diamond: c1 -> c2, c1 -> c3, c2 -> c4, c3 -> c4; references to base and deleted columns are ignored.
        Assert.Null(FormulaDependencies.FindCycle(Refs(
            ("c1", new[] { "c2", "c3", "amount" }),
            ("c2", new[] { "c4" }),
            ("c3", new[] { "c4", "c99" }),
            ("c4", new[] { "date" }))));
        Assert.Null(FormulaDependencies.FindCycle(Refs()));
    }

    [Fact]
    public void Cycle_check_works_from_compiled_formulas()
    {
        var table = Ledger();
        var formulas = new Dictionary<string, CompiledFormula>
        {
            ["c10"] = table.Compile("[Amount] + [#c11]"),
            ["c11"] = table.Compile("SUM([#c10])"),
            ["c12"] = table.Compile("[Amount]"),
        };
        Assert.Equal(new[] { "c10", "c11", "c10" }, FormulaDependencies.FindCycle(formulas));
    }

    [Fact]
    public void Runtime_cycles_evaluate_to_circ_instead_of_overflowing_the_stack()
    {
        var sheet = Ledger()
            .Formula("c10", "A", "[B] + 1")
            .Formula("c11", "B", "[A] + 1")
            .Formula("c12", "Self", "[#c12] + 1")
            .Formula("c13", "Self Sum", "SUM([Self Sum])")
            .Formula("c14", "Uses A", "[A] * 2")
            .Formula("c15", "Fine", "[Amount] * 2")
            .Sheet();

        for (var row = 0; row < 5; row++)
        {
            Expect.Error(FormulaErrors.Circular, sheet.Get("c10", row));
            Expect.Error(FormulaErrors.Circular, sheet.Get("c11", row));
            Expect.Error(FormulaErrors.Circular, sheet.Get("c12", row));
            Expect.Error(FormulaErrors.Circular, sheet.Get("c13", row));
            Expect.Error(FormulaErrors.Circular, sheet.Get("c14", row));
        }

        Expect.Number("100", sheet.Get("c15", 0));
    }

    [Fact]
    public void Static_check_is_conservative_but_runtime_only_flags_cycles_actually_taken()
    {
        var table = Ledger()
            .Formula("c10", "A", "IF(FALSE, [B], 1)")
            .Formula("c11", "B", "[A] + 1");
        Assert.NotNull(FormulaDependencies.FindCycle(new Dictionary<string, CompiledFormula>
        {
            ["c10"] = table.Compile("IF(FALSE, [B], 1)"),
            ["c11"] = table.Compile("[A] + 1"),
        }));

        var sheet = table.Sheet();
        Expect.Number("1", sheet.Get("c10", 0));
        Expect.Number("2", sheet.Get("c11", 0));
    }

    [Fact]
    public void Long_chains_of_formula_columns_evaluate_and_very_long_ones_fail_safely()
    {
        static FormulaSheet Chain(int length)
        {
            var formulas = new Dictionary<string, CompiledFormula>();
            formulas["f0"] = FormulaCompiler.Compile("[#amount]", _ => null).Formula!;
            for (var i = 1; i < length; i++)
            {
                formulas["f" + i] = FormulaCompiler.Compile($"[#f{i - 1}] + 1", _ => null).Formula!;
            }

            var bases = new Dictionary<string, Func<int, FormulaValue>> { ["amount"] = _ => new FormulaValue.Number(0m) };
            return new FormulaSheet(1, bases, formulas);
        }

        Expect.Number("199", Chain(200).Get("f199", 0));

        var deep = Chain(2000).Get("f1999", 0);
        Expect.Error(FormulaErrors.Value, deep);
    }
}
