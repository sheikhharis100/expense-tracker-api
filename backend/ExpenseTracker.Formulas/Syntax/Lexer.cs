using System.Globalization;
using System.Text;

namespace ExpenseTracker.Formulas.Syntax;

internal enum TokenKind
{
    Number,
    Text,
    Ref,
    Ident,
    Op,
    LParen,
    RParen,
    Comma,
    End,
}

/// <param name="Kind">Token kind.</param>
/// <param name="Position">1-based position of the first character in the original formula text.</param>
/// <param name="Lexeme">The source text of the token (used in error messages).</param>
/// <param name="Number">Parsed value of a number token.</param>
/// <param name="Value">Unescaped text of a string token, or the raw body (between the brackets) of a reference token.</param>
internal readonly record struct Token(TokenKind Kind, int Position, string Lexeme, decimal Number = 0m, string? Value = null);

/// <summary>Thrown internally by the lexer/parser; always caught by <see cref="FormulaCompiler"/>.</summary>
internal sealed class FormulaParseException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal static class Lexer
{
    public static List<Token> Tokenize(string text, int start)
    {
        var tokens = new List<Token>();
        var i = start;
        while (true)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i >= text.Length)
            {
                tokens.Add(new Token(TokenKind.End, text.Length + 1, string.Empty));
                return tokens;
            }

            var c = text[i];
            var pos = i + 1;

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                var s = i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                if (i < text.Length && text[i] == '.')
                {
                    i++;
                    while (i < text.Length && char.IsAsciiDigit(text[i]))
                    {
                        i++;
                    }
                }

                var lexeme = text[s..i];
                if (!decimal.TryParse(lexeme, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    throw new FormulaParseException(FormulaErrors.Num,
                        $"The number {Shorten(lexeme)} at position {pos} is too large.");
                }

                tokens.Add(new Token(TokenKind.Number, pos, lexeme, number));
                continue;
            }

            if (c == '"')
            {
                var sb = new StringBuilder();
                var j = i + 1;
                var closed = false;
                while (j < text.Length)
                {
                    if (text[j] == '"')
                    {
                        if (j + 1 < text.Length && text[j + 1] == '"')
                        {
                            sb.Append('"');
                            j += 2;
                            continue;
                        }

                        j++;
                        closed = true;
                        break;
                    }

                    sb.Append(text[j]);
                    j++;
                }

                if (!closed)
                {
                    throw Syntax($"The text starting at position {pos} is missing its closing quote (\").");
                }

                tokens.Add(new Token(TokenKind.Text, pos, text[i..j], Value: sb.ToString()));
                i = j;
                continue;
            }

            if (c == '[')
            {
                var close = -1;
                for (var j = i + 1; j < text.Length; j++)
                {
                    if (text[j] == ']')
                    {
                        close = j;
                        break;
                    }

                    if (text[j] == '[')
                    {
                        throw Syntax($"Unexpected '[' at position {j + 1} inside the column reference that starts at position {pos}.");
                    }
                }

                if (close < 0)
                {
                    throw Syntax($"Missing ']' for the column reference starting at position {pos}.");
                }

                tokens.Add(new Token(TokenKind.Ref, pos, text[i..(close + 1)], Value: text[(i + 1)..close]));
                i = close + 1;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var s = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Ident, pos, text[s..i]));
                continue;
            }

            switch (c)
            {
                case '(':
                    tokens.Add(new Token(TokenKind.LParen, pos, "("));
                    i++;
                    continue;
                case ')':
                    tokens.Add(new Token(TokenKind.RParen, pos, ")"));
                    i++;
                    continue;
                case ',':
                    tokens.Add(new Token(TokenKind.Comma, pos, ","));
                    i++;
                    continue;
                case '+':
                case '-':
                case '*':
                case '/':
                case '&':
                case '=':
                    tokens.Add(new Token(TokenKind.Op, pos, c.ToString()));
                    i++;
                    continue;
                case '<':
                    if (i + 1 < text.Length && (text[i + 1] == '=' || text[i + 1] == '>'))
                    {
                        tokens.Add(new Token(TokenKind.Op, pos, text.Substring(i, 2)));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.Op, pos, "<"));
                        i++;
                    }

                    continue;
                case '>':
                    if (i + 1 < text.Length && text[i + 1] == '=')
                    {
                        tokens.Add(new Token(TokenKind.Op, pos, ">="));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.Op, pos, ">"));
                        i++;
                    }

                    continue;
                case '^':
                    throw Syntax($"The '^' (power) operator at position {pos} is not supported.");
                case '%':
                    throw Syntax($"The '%' operator at position {pos} is not supported; divide by 100 instead.");
                case ';':
                    throw Syntax($"Unexpected ';' at position {pos}. Use ',' to separate function arguments.");
                case ':':
                    throw Syntax($"Unexpected ':' at position {pos}. Cell ranges are not supported; use a column reference such as [Amount].");
                case ']':
                    throw Syntax($"Unexpected ']' at position {pos} without a matching '['.");
                case '{':
                case '}':
                    throw Syntax($"Unexpected '{c}' at position {pos}. Arrays are not supported.");
                default:
                    throw Syntax($"Unexpected character {DescribeChar(c)} at position {pos}.");
            }
        }
    }

    internal static FormulaParseException Syntax(string message) => new(FormulaErrors.Syntax, message);

    internal static string Shorten(string s) => s.Length <= 40 ? s : s[..37] + "...";

    private static string DescribeChar(char c) =>
        char.IsControl(c) || char.IsSurrogate(c) || char.IsWhiteSpace(c)
            ? $"U+{(int)c:X4}"
            : $"'{c}'";
}
