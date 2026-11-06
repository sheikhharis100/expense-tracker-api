using System.Runtime.CompilerServices;
using ExpenseTracker.Formulas.Syntax;

namespace ExpenseTracker.Formulas.Evaluation;

/// <summary>Tree-walking evaluator for one formula cell (one row of one formula column).</summary>
internal sealed class Evaluator
{
    private readonly FormulaSheet _sheet;
    private readonly int _row;

    public Evaluator(FormulaSheet sheet, int row)
    {
        _sheet = sheet;
        _row = row;
    }

    public FormulaValue Evaluate(Node node)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            return ValueOps.Error(FormulaErrors.Value, "The formula is too complex to evaluate.");
        }

        return node switch
        {
            NumberNode n => n.Constant,
            TextNode t => t.Constant,
            BoolNode b => FormulaValue.Bool.Of(b.Value),
            RefNode r => _sheet.Get(r.Key, _row),
            GroupNode g => Evaluate(g.Inner),
            UnaryNode u => EvaluateUnary(u),
            BinaryNode b => EvaluateBinary(b),
            CallNode c => EvaluateCall(c),
            _ => ValueOps.Error(FormulaErrors.Value, "Unsupported expression."),
        };
    }

    private FormulaValue EvaluateUnary(UnaryNode u)
    {
        var v = Evaluate(u.Operand);
        if (ValueOps.ToNumber(v, out var x) is { } error)
        {
            return error;
        }

        return new FormulaValue.Number(u.Op == '-' ? -x : x);
    }

    private FormulaValue EvaluateBinary(BinaryNode b)
    {
        var left = Evaluate(b.Left);
        if (left is FormulaValue.Error)
        {
            return left;
        }

        var right = Evaluate(b.Right);
        if (right is FormulaValue.Error)
        {
            return right;
        }

        switch (b.Op)
        {
            case BinaryOp.Concat:
            {
                var l = ValueOps.ToText(left);
                var r = ValueOps.ToText(right);
                if ((long)l.Length + r.Length > ValueOps.MaxTextLength)
                {
                    return ValueOps.Error(FormulaErrors.Value,
                        $"The text is too long (at most {ValueOps.MaxTextLength:N0} characters).");
                }

                return new FormulaValue.Text(l + r);
            }

            case BinaryOp.Equal:
                return FormulaValue.Bool.Of(ValueOps.Compare(left, right) == 0);
            case BinaryOp.NotEqual:
                return FormulaValue.Bool.Of(ValueOps.Compare(left, right) != 0);
            case BinaryOp.Less:
                return FormulaValue.Bool.Of(ValueOps.Compare(left, right) < 0);
            case BinaryOp.Greater:
                return FormulaValue.Bool.Of(ValueOps.Compare(left, right) > 0);
            case BinaryOp.LessOrEqual:
                return FormulaValue.Bool.Of(ValueOps.Compare(left, right) <= 0);
            case BinaryOp.GreaterOrEqual:
                return FormulaValue.Bool.Of(ValueOps.Compare(left, right) >= 0);
        }

        if (ValueOps.ToNumber(left, out var x) is { } e1)
        {
            return e1;
        }

        if (ValueOps.ToNumber(right, out var y) is { } e2)
        {
            return e2;
        }

        try
        {
            return b.Op switch
            {
                BinaryOp.Add => new FormulaValue.Number(x + y),
                BinaryOp.Subtract => new FormulaValue.Number(x - y),
                BinaryOp.Multiply => new FormulaValue.Number(x * y),
                BinaryOp.Divide => y == 0m
                    ? ValueOps.Error(FormulaErrors.DivideByZero, "Division by zero.")
                    : new FormulaValue.Number(x / y),
                _ => ValueOps.Error(FormulaErrors.Value, "Unsupported operator."),
            };
        }
        catch (OverflowException)
        {
            return NumberTooLarge();
        }
    }

    private FormulaValue EvaluateCall(CallNode c)
    {
        // Aggregates over whole columns give the same answer on every row: compute them once per sheet.
        var cacheable = c.IsAggregate && !c.UsesRow;
        if (cacheable && _sheet.TryGetCached(c, out var cached))
        {
            return cached;
        }

        var result = c.Function switch
        {
            FunctionId.Sum or FunctionId.Average or FunctionId.Min or FunctionId.Max or FunctionId.Count => Aggregate(c),
            FunctionId.If => If(c),
            FunctionId.Round => Round(c),
            FunctionId.Abs => Abs(c),
            FunctionId.SumIf => SumIf(c),
            FunctionId.SumIfs => SumIfs(c),
            _ => ValueOps.Error(FormulaErrors.Name, $"Unknown function '{c.Name}'."),
        };

        if (cacheable)
        {
            _sheet.StoreCached(c, result);
        }

        return result;
    }

    private FormulaValue If(CallNode c)
    {
        var condition = Evaluate(c.Args[0]);
        if (ValueOps.ToCondition(condition, out var isTrue) is { } error)
        {
            return error;
        }

        // Only the chosen branch is evaluated.
        if (isTrue)
        {
            return Evaluate(c.Args[1]);
        }

        return c.Args.Count > 2 ? Evaluate(c.Args[2]) : FormulaValue.Bool.False;
    }

    private FormulaValue Round(CallNode c)
    {
        var value = Evaluate(c.Args[0]);
        if (ValueOps.ToNumber(value, out var x) is { } e1)
        {
            return e1;
        }

        var digitsValue = Evaluate(c.Args[1]);
        if (ValueOps.ToNumber(digitsValue, out var d) is { } e2)
        {
            return e2;
        }

        if (d != decimal.Truncate(d) || d < -10m || d > 10m)
        {
            return ValueOps.Error(FormulaErrors.Value, "ROUND digits must be a whole number between -10 and 10.");
        }

        var digits = (int)d;
        try
        {
            if (digits >= 0)
            {
                return new FormulaValue.Number(decimal.Round(x, digits, MidpointRounding.AwayFromZero));
            }

            var factor = 1m;
            for (var i = 0; i < -digits; i++)
            {
                factor *= 10m;
            }

            return new FormulaValue.Number(decimal.Round(x / factor, 0, MidpointRounding.AwayFromZero) * factor);
        }
        catch (OverflowException)
        {
            return NumberTooLarge();
        }
    }

    private FormulaValue Abs(CallNode c)
    {
        var value = Evaluate(c.Args[0]);
        if (ValueOps.ToNumber(value, out var x) is { } error)
        {
            return error;
        }

        return new FormulaValue.Number(Math.Abs(x));
    }

    /// <summary>SUM, AVERAGE, MIN, MAX and COUNT.</summary>
    private FormulaValue Aggregate(CallNode c)
    {
        var sum = 0m;
        var count = 0;
        decimal? min = null;
        decimal? max = null;
        var isCount = c.Function == FunctionId.Count;

        try
        {
            foreach (var arg in c.Args)
            {
                var target = CallNode.Unwrap(arg);
                if (target is RefNode reference)
                {
                    // Column references (whole column, or [@...] for the current row) skip blanks, text and booleans.
                    if (!reference.CurrentRow)
                    {
                        if (!_sheet.HasColumn(reference.Key))
                        {
                            return _sheet.MissingColumn(reference.Key);
                        }

                        for (var row = 0; row < _sheet.RowCount; row++)
                        {
                            var cell = _sheet.Get(reference.Key, row);
                            if (cell is FormulaValue.Error)
                            {
                                return cell;
                            }

                            if (cell is FormulaValue.Number n)
                            {
                                Add(n.Value);
                            }
                        }
                    }
                    else
                    {
                        var cell = _sheet.Get(reference.Key, _row);
                        if (cell is FormulaValue.Error)
                        {
                            return cell;
                        }

                        if (cell is FormulaValue.Number n)
                        {
                            Add(n.Value);
                        }
                    }

                    continue;
                }

                // Values typed or calculated in the formula are converted like arithmetic (blank is skipped).
                var value = Evaluate(arg);
                switch (value)
                {
                    case FormulaValue.Error:
                        return value;
                    case FormulaValue.Blank:
                        break;
                    case FormulaValue.Number n:
                        Add(n.Value);
                        break;
                    case FormulaValue.Bool b:
                        Add(b.Value ? 1m : 0m);
                        break;
                    case FormulaValue.Text t when ValueOps.TryParseNumber(t.Value, out var parsed):
                        Add(parsed);
                        break;
                    case FormulaValue.Text t:
                        if (!isCount)
                        {
                            return ValueOps.Error(FormulaErrors.Value,
                                $"{c.Name}: \"{ValueOps.Shorten(t.Value)}\" is not a number.");
                        }

                        break;
                }
            }
        }
        catch (OverflowException)
        {
            return NumberTooLarge();
        }

        return c.Function switch
        {
            FunctionId.Sum => new FormulaValue.Number(sum),
            FunctionId.Count => new FormulaValue.Number(count),
            FunctionId.Min => new FormulaValue.Number(min ?? 0m),
            FunctionId.Max => new FormulaValue.Number(max ?? 0m),
            _ => count == 0
                ? ValueOps.Error(FormulaErrors.DivideByZero, "AVERAGE has no numbers to average.")
                : new FormulaValue.Number(sum / count),
        };

        void Add(decimal v)
        {
            count++;
            if (!isCount)
            {
                sum += v;
                min = min is { } m1 && m1 <= v ? m1 : v;
                max = max is { } m2 && m2 >= v ? m2 : v;
            }
        }
    }

    private FormulaValue SumIf(CallNode c)
    {
        var range = (RefNode)CallNode.Unwrap(c.Args[0]);
        var sumRange = c.Args.Count > 2 ? (RefNode)CallNode.Unwrap(c.Args[2]) : range;
        return ConditionalSum(sumRange, [(range, c.Args[1])]);
    }

    private FormulaValue SumIfs(CallNode c)
    {
        var sumRange = (RefNode)CallNode.Unwrap(c.Args[0]);
        var pairs = new List<(RefNode Range, Node Criteria)>();
        for (var i = 1; i + 1 < c.Args.Count; i += 2)
        {
            pairs.Add(((RefNode)CallNode.Unwrap(c.Args[i]), c.Args[i + 1]));
        }

        return ConditionalSum(sumRange, pairs);
    }

    /// <summary>
    /// Sums the numeric cells of <paramref name="sumRange"/> on rows where every criterion matches.
    /// An error in any criteria column cell, or in a summed cell of a matching row, is returned.
    /// </summary>
    private FormulaValue ConditionalSum(RefNode sumRange, IReadOnlyList<(RefNode Range, Node Criteria)> pairs)
    {
        var criteria = new Criterion[pairs.Count];
        for (var i = 0; i < pairs.Count; i++)
        {
            if (!_sheet.HasColumn(pairs[i].Range.Key))
            {
                return _sheet.MissingColumn(pairs[i].Range.Key);
            }

            var value = Evaluate(pairs[i].Criteria);
            if (value is FormulaValue.Error)
            {
                return value;
            }

            criteria[i] = Criterion.From(value);
        }

        if (!_sheet.HasColumn(sumRange.Key))
        {
            return _sheet.MissingColumn(sumRange.Key);
        }

        var sum = 0m;
        try
        {
            for (var row = 0; row < _sheet.RowCount; row++)
            {
                var matches = true;
                for (var i = 0; i < pairs.Count; i++)
                {
                    var cell = _sheet.Get(pairs[i].Range.Key, row);
                    if (cell is FormulaValue.Error)
                    {
                        return cell;
                    }

                    if (matches && !criteria[i].Matches(cell))
                    {
                        matches = false;
                    }
                }

                if (!matches)
                {
                    continue;
                }

                var summed = _sheet.Get(sumRange.Key, row);
                if (summed is FormulaValue.Error)
                {
                    return summed;
                }

                if (summed is FormulaValue.Number n)
                {
                    sum += n.Value;
                }
            }
        }
        catch (OverflowException)
        {
            return NumberTooLarge();
        }

        return new FormulaValue.Number(sum);
    }

    private static FormulaValue.Error NumberTooLarge() =>
        ValueOps.Error(FormulaErrors.Num, "The number is too large.");
}
