namespace ExpenseTracker.Formulas.Syntax;

/// <summary>Base class of the formula syntax tree. Positions are 1-based character offsets in the source text.</summary>
internal abstract class Node(int position)
{
    public int Position { get; } = position;

    /// <summary>True when the value of this node depends on the current row (it contains a scalar column reference).</summary>
    public abstract bool UsesRow { get; }
}

internal sealed class NumberNode(int position, decimal value) : Node(position)
{
    public decimal Value { get; } = value;
    public FormulaValue.Number Constant { get; } = new(value);
    public override bool UsesRow => false;
}

internal sealed class TextNode(int position, string value) : Node(position)
{
    public string Value { get; } = value;
    public FormulaValue.Text Constant { get; } = new(value);
    public override bool UsesRow => false;
}

internal sealed class BoolNode(int position, bool value) : Node(position)
{
    public bool Value { get; } = value;
    public override bool UsesRow => false;
}

/// <summary>A column reference. <see cref="CurrentRow"/> is true for the explicit <c>[@...]</c> form.</summary>
internal sealed class RefNode(int position, string key, bool currentRow) : Node(position)
{
    public string Key { get; } = key;
    public bool CurrentRow { get; } = currentRow;

    // In a scalar position every reference reads the current row.
    public override bool UsesRow => true;
}

/// <summary>Explicit parentheses, kept so the canonical text preserves the user's grouping.</summary>
internal sealed class GroupNode(int position, Node inner) : Node(position)
{
    public Node Inner { get; } = inner;
    public override bool UsesRow => Inner.UsesRow;
}

internal sealed class UnaryNode(int position, char op, Node operand) : Node(position)
{
    public char Op { get; } = op;
    public Node Operand { get; } = operand;
    public override bool UsesRow => Operand.UsesRow;
}

internal enum BinaryOp
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Concat,
    Equal,
    NotEqual,
    Less,
    Greater,
    LessOrEqual,
    GreaterOrEqual,
}

internal sealed class BinaryNode(int position, BinaryOp op, Node left, Node right) : Node(position)
{
    private readonly bool _usesRow = left.UsesRow || right.UsesRow;

    public BinaryOp Op { get; } = op;
    public Node Left { get; } = left;
    public Node Right { get; } = right;
    public override bool UsesRow => _usesRow;

    public static string Symbol(BinaryOp op) => op switch
    {
        BinaryOp.Add => "+",
        BinaryOp.Subtract => "-",
        BinaryOp.Multiply => "*",
        BinaryOp.Divide => "/",
        BinaryOp.Concat => "&",
        BinaryOp.Equal => "=",
        BinaryOp.NotEqual => "<>",
        BinaryOp.Less => "<",
        BinaryOp.Greater => ">",
        BinaryOp.LessOrEqual => "<=",
        BinaryOp.GreaterOrEqual => ">=",
        _ => "?",
    };
}

internal enum FunctionId
{
    Sum,
    Average,
    Min,
    Max,
    Count,
    If,
    Round,
    Abs,
    SumIf,
    SumIfs,
}

internal sealed class CallNode : Node
{
    public CallNode(int position, FunctionId function, string name, IReadOnlyList<Node> args)
        : base(position)
    {
        Function = function;
        Name = name;
        Args = args;
        _usesRow = ComputeUsesRow(function, args);
    }

    private readonly bool _usesRow;

    public FunctionId Function { get; }
    public string Name { get; }
    public IReadOnlyList<Node> Args { get; }
    public override bool UsesRow => _usesRow;

    /// <summary>True for functions whose arguments may be whole columns (and are therefore worth caching).</summary>
    public bool IsAggregate => Function is FunctionId.Sum or FunctionId.Average or FunctionId.Min
        or FunctionId.Max or FunctionId.Count or FunctionId.SumIf or FunctionId.SumIfs;

    /// <summary>Strips redundant parentheses: <c>(([Amount]))</c> → <c>[Amount]</c>.</summary>
    public static Node Unwrap(Node node)
    {
        while (node is GroupNode g)
        {
            node = g.Inner;
        }

        return node;
    }

    /// <summary>A plain (non-@) column reference written directly as an argument: means the whole column.</summary>
    public static bool IsWholeColumn(Node arg) => Unwrap(arg) is RefNode { CurrentRow: false };

    /// <summary>Whether argument <paramref name="index"/> of <paramref name="function"/> is a range (column) argument.</summary>
    public static bool IsRangeArgument(FunctionId function, int index) => function switch
    {
        FunctionId.SumIf => index == 0 || index == 2,
        FunctionId.SumIfs => index == 0 || index % 2 == 1,
        _ => false,
    };

    private static bool ComputeUsesRow(FunctionId function, IReadOnlyList<Node> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            var isRange = IsRangeArgument(function, i)
                || (function is FunctionId.Sum or FunctionId.Average or FunctionId.Min
                    or FunctionId.Max or FunctionId.Count && IsWholeColumn(arg));
            if (!isRange && arg.UsesRow)
            {
                return true;
            }
        }

        return false;
    }
}
