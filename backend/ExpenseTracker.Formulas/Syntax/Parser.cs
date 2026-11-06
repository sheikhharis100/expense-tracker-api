namespace ExpenseTracker.Formulas.Syntax;

/// <summary>
/// Recursive-descent parser. Grammar (lowest to highest precedence):
/// <code>
/// comparison     := concat (('=' | '&lt;&gt;' | '&lt;' | '&gt;' | '&lt;=' | '&gt;=') concat)*
/// concat         := additive ('&amp;' additive)*
/// additive       := multiplicative (('+' | '-') multiplicative)*
/// multiplicative := unary (('*' | '/') unary)*
/// unary          := ('+' | '-') unary | primary
/// primary        := number | text | TRUE | FALSE | reference | function '(' [args] ')' | '(' comparison ')'
/// </code>
/// </summary>
internal sealed class Parser
{
    private static readonly Dictionary<string, FunctionId> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SUM"] = FunctionId.Sum,
        ["AVERAGE"] = FunctionId.Average,
        ["MIN"] = FunctionId.Min,
        ["MAX"] = FunctionId.Max,
        ["COUNT"] = FunctionId.Count,
        ["IF"] = FunctionId.If,
        ["ROUND"] = FunctionId.Round,
        ["ABS"] = FunctionId.Abs,
        ["SUMIF"] = FunctionId.SumIf,
        ["SUMIFS"] = FunctionId.SumIfs,
    };

    private readonly List<Token> _tokens;
    private readonly Func<string, string?> _resolveName;
    private int _index;
    private int _depth;

    public Parser(List<Token> tokens, Func<string, string?> resolveName)
    {
        _tokens = tokens;
        _resolveName = resolveName;
    }

    public HashSet<string> References { get; } = new(StringComparer.Ordinal);

    private Token Peek => _tokens[_index];

    public Node ParseFormula()
    {
        if (Peek.Kind == TokenKind.End)
        {
            throw Lexer.Syntax("The formula is empty.");
        }

        var node = ParseComparison();
        if (Peek.Kind != TokenKind.End)
        {
            throw Unexpected(Peek);
        }

        return node;
    }

    private Token Next()
    {
        var t = _tokens[_index];
        if (t.Kind != TokenKind.End)
        {
            _index++;
        }

        return t;
    }

    private void Enter(int position)
    {
        _depth++;
        if (_depth > FormulaCompiler.MaxDepth)
        {
            throw Lexer.Syntax(
                $"The formula is nested too deeply at position {position} (at most {FormulaCompiler.MaxDepth} levels of parentheses, functions and signs).");
        }
    }

    private void Exit() => _depth--;

    private static bool IsOp(Token t, string op) => t.Kind == TokenKind.Op && t.Lexeme == op;

    private Node ParseComparison()
    {
        var left = ParseConcat();
        while (Peek.Kind == TokenKind.Op && Peek.Lexeme is "=" or "<>" or "<" or ">" or "<=" or ">=")
        {
            var op = Next();
            var kind = op.Lexeme switch
            {
                "=" => BinaryOp.Equal,
                "<>" => BinaryOp.NotEqual,
                "<" => BinaryOp.Less,
                ">" => BinaryOp.Greater,
                "<=" => BinaryOp.LessOrEqual,
                _ => BinaryOp.GreaterOrEqual,
            };
            left = new BinaryNode(op.Position, kind, left, ParseConcat());
        }

        return left;
    }

    private Node ParseConcat()
    {
        var left = ParseAdditive();
        while (IsOp(Peek, "&"))
        {
            var op = Next();
            left = new BinaryNode(op.Position, BinaryOp.Concat, left, ParseAdditive());
        }

        return left;
    }

    private Node ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (IsOp(Peek, "+") || IsOp(Peek, "-"))
        {
            var op = Next();
            var kind = op.Lexeme == "+" ? BinaryOp.Add : BinaryOp.Subtract;
            left = new BinaryNode(op.Position, kind, left, ParseMultiplicative());
        }

        return left;
    }

    private Node ParseMultiplicative()
    {
        var left = ParseUnary();
        while (IsOp(Peek, "*") || IsOp(Peek, "/"))
        {
            var op = Next();
            var kind = op.Lexeme == "*" ? BinaryOp.Multiply : BinaryOp.Divide;
            left = new BinaryNode(op.Position, kind, left, ParseUnary());
        }

        return left;
    }

    private Node ParseUnary()
    {
        if (IsOp(Peek, "-") || IsOp(Peek, "+"))
        {
            var op = Next();
            Enter(op.Position);
            var operand = ParseUnary();
            Exit();
            return new UnaryNode(op.Position, op.Lexeme[0], operand);
        }

        return ParsePrimary();
    }

    private Node ParsePrimary()
    {
        var t = Peek;
        switch (t.Kind)
        {
            case TokenKind.Number:
                Next();
                return new NumberNode(t.Position, t.Number);

            case TokenKind.Text:
                Next();
                return new TextNode(t.Position, t.Value ?? string.Empty);

            case TokenKind.Ref:
                Next();
                return ParseReference(t);

            case TokenKind.Ident:
                return ParseIdentifier();

            case TokenKind.LParen:
            {
                Next();
                Enter(t.Position);
                var inner = ParseComparison();
                var close = Peek;
                if (close.Kind != TokenKind.RParen)
                {
                    throw close.Kind == TokenKind.End
                        ? Lexer.Syntax($"Expected ')' at position {close.Position} to close the '(' at position {t.Position}.")
                        : Lexer.Syntax($"Expected ')' at position {close.Position}, but found {Describe(close)}.");
                }

                Next();
                Exit();
                return new GroupNode(t.Position, inner);
            }

            default:
                throw ExpectedValue(t);
        }
    }

    private Node ParseReference(Token t)
    {
        var body = (t.Value ?? string.Empty).Trim();
        var currentRow = false;
        if (body.StartsWith('@'))
        {
            currentRow = true;
            body = body[1..].Trim();
        }

        string key;
        if (body.StartsWith('#'))
        {
            key = body[1..].Trim();
            if (key.Length == 0)
            {
                throw Lexer.Syntax($"Missing column key after '#' in the reference at position {t.Position}.");
            }

            if (!FormulaCompiler.IsValidKey(key))
            {
                throw Lexer.Syntax($"'{Lexer.Shorten(key)}' at position {t.Position} is not a valid column key.");
            }
        }
        else
        {
            if (body.Length == 0)
            {
                throw Lexer.Syntax($"Empty column reference at position {t.Position}.");
            }

            var resolved = _resolveName(body);
            if (string.IsNullOrEmpty(resolved) || !FormulaCompiler.IsValidKey(resolved))
            {
                throw new FormulaParseException(FormulaErrors.Name,
                    $"Unknown column [{Lexer.Shorten(body)}] at position {t.Position}.");
            }

            key = resolved;
        }

        References.Add(key);
        return new RefNode(t.Position, key, currentRow);
    }

    private Node ParseIdentifier()
    {
        var t = Next();
        if (Peek.Kind == TokenKind.LParen)
        {
            return ParseCall(t);
        }

        if (t.Lexeme.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return new BoolNode(t.Position, true);
        }

        if (t.Lexeme.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new BoolNode(t.Position, false);
        }

        var name = Lexer.Shorten(t.Lexeme);
        throw new FormulaParseException(FormulaErrors.Name,
            $"Unknown name '{name}' at position {t.Position}. Column names must be written in square brackets, for example [{name}].");
    }

    private Node ParseCall(Token nameToken)
    {
        var name = nameToken.Lexeme.ToUpperInvariant();
        if (!Functions.TryGetValue(name, out var function))
        {
            throw new FormulaParseException(FormulaErrors.Name,
                $"Unknown function '{Lexer.Shorten(name)}' at position {nameToken.Position}.");
        }

        var open = Next();
        Enter(open.Position);
        var args = new List<Node>();
        if (Peek.Kind == TokenKind.RParen)
        {
            Next();
        }
        else
        {
            while (true)
            {
                args.Add(ParseComparison());
                var t = Peek;
                if (t.Kind == TokenKind.Comma)
                {
                    Next();
                    continue;
                }

                if (t.Kind == TokenKind.RParen)
                {
                    Next();
                    break;
                }

                throw t.Kind == TokenKind.End
                    ? Lexer.Syntax($"Expected ')' at position {t.Position} to close {name}( at position {nameToken.Position}.")
                    : Lexer.Syntax($"Expected ',' or ')' at position {t.Position}, but found {Describe(t)}.");
            }
        }

        Exit();
        Validate(function, name, nameToken.Position, args);
        return new CallNode(nameToken.Position, function, name, args);
    }

    private static void Validate(FunctionId function, string name, int position, List<Node> args)
    {
        var n = args.Count;
        var arityError = function switch
        {
            FunctionId.Sum or FunctionId.Average or FunctionId.Min or FunctionId.Max or FunctionId.Count
                => n < 1 ? "needs at least 1 argument" : null,
            FunctionId.If => n is < 2 or > 3 ? "takes 2 or 3 arguments: IF(condition, value_if_true, [value_if_false])" : null,
            FunctionId.Round => n != 2 ? "takes exactly 2 arguments: ROUND(number, digits)" : null,
            FunctionId.Abs => n != 1 ? "takes exactly 1 argument: ABS(number)" : null,
            FunctionId.SumIf => n is < 2 or > 3 ? "takes 2 or 3 arguments: SUMIF(range, criteria, [sum_range])" : null,
            FunctionId.SumIfs => n < 3 || n % 2 == 0
                ? "takes a sum range followed by pairs of range and criteria: SUMIFS(sum_range, criteria_range1, criteria1, ...)"
                : null,
            _ => null,
        };

        if (arityError is not null)
        {
            var given = n == 1 ? "1 argument was" : $"{n} arguments were";
            throw Lexer.Syntax($"{name} at position {position} {arityError}, but {given} given.");
        }

        for (var i = 0; i < n; i++)
        {
            if (!CallNode.IsRangeArgument(function, i))
            {
                continue;
            }

            var arg = CallNode.Unwrap(args[i]);
            if (arg is RefNode { CurrentRow: true })
            {
                throw new FormulaParseException(FormulaErrors.Value,
                    $"Argument {i + 1} of {name} at position {args[i].Position} must be a whole column such as [Amount], not a current-row reference [@...].");
            }

            if (arg is not RefNode)
            {
                throw new FormulaParseException(FormulaErrors.Value,
                    $"Argument {i + 1} of {name} at position {args[i].Position} must be a column reference such as [Amount].");
            }
        }
    }

    private FormulaParseException Unexpected(Token t) => t.Kind switch
    {
        TokenKind.RParen => Lexer.Syntax($"Unexpected ')' at position {t.Position}: there is no matching '('."),
        TokenKind.Comma => Lexer.Syntax($"Unexpected ',' at position {t.Position}: commas can only separate function arguments."),
        TokenKind.Op => Lexer.Syntax($"Unexpected {Describe(t)} at position {t.Position}."),
        _ => Lexer.Syntax($"Unexpected {Describe(t)} at position {t.Position}. Is an operator missing?"),
    };

    private FormulaParseException ExpectedValue(Token t)
    {
        var found = t.Kind == TokenKind.End ? "the end of the formula" : Describe(t);
        var after = _index > 0 ? $" after {Describe(_tokens[_index - 1])}" : string.Empty;
        return Lexer.Syntax($"Expected a value{after} at position {t.Position}, but found {found}.");
    }

    private static string Describe(Token t) =>
        t.Kind == TokenKind.End ? "the end of the formula" : $"'{Lexer.Shorten(t.Lexeme)}'";
}
