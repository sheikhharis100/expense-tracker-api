using System.Globalization;
using System.Text;
using ExpenseTracker.Formulas.Syntax;

namespace ExpenseTracker.Formulas;

/// <summary>A parsed, validated formula. Immutable and safe to share between sheets and threads.</summary>
public sealed class CompiledFormula
{
    internal CompiledFormula(Node root, string canonical, IReadOnlySet<string> references)
    {
        Root = root;
        Canonical = canonical;
        References = references;
    }

    /// <summary>
    /// The formula text with every column reference rewritten to its stable key form
    /// (<c>[#key]</c> or <c>[@#key]</c>), normalised spacing and upper-case function names,
    /// without a leading <c>=</c>. Store this; compiling it again yields the same text.
    /// </summary>
    public string Canonical { get; }

    /// <summary>The column keys this formula references (scalar and whole-column references alike).</summary>
    public IReadOnlySet<string> References { get; }

    internal Node Root { get; }

    public override string ToString() => Canonical;
}

/// <summary>The outcome of <see cref="FormulaCompiler.Compile"/>: exactly one of the two is non-null.</summary>
public sealed record CompileResult(CompiledFormula? Formula, FormulaValue.Error? Error)
{
    /// <summary>True when the formula compiled.</summary>
    public bool Success => Formula is not null;
}

/// <summary>Compiles formula text into a <see cref="CompiledFormula"/> and converts between canonical and display text.</summary>
public static class FormulaCompiler
{
    /// <summary>Maximum formula length in characters.</summary>
    public const int MaxLength = 1000;

    /// <summary>Maximum nesting of parentheses, function calls and unary signs.</summary>
    public const int MaxDepth = 64;

    /// <summary>Maximum length of a column key.</summary>
    public const int MaxKeyLength = 100;

    /// <summary>
    /// Compiles <paramref name="text"/>. Never throws for any input text: problems are reported
    /// through <see cref="CompileResult.Error"/> (<c>#SYNTAX!</c>, <c>#NAME?</c>, <c>#VALUE!</c> or <c>#NUM!</c>).
    /// </summary>
    /// <param name="text">The formula, optionally starting with <c>=</c>.</param>
    /// <param name="resolveName">
    /// Maps a display name (without brackets, already trimmed) to a column key, or returns null when no
    /// column has that name. It should be case-insensitive; see <see cref="CreateNameResolver"/>.
    /// </param>
    public static CompileResult Compile(string text, Func<string, string?> resolveName)
    {
        ArgumentNullException.ThrowIfNull(resolveName);
        text ??= string.Empty;

        if (text.Length > MaxLength)
        {
            return Fail(FormulaErrors.Syntax,
                $"The formula is too long ({text.Length} characters; the maximum is {MaxLength}).");
        }

        try
        {
            var start = 0;
            while (start < text.Length && char.IsWhiteSpace(text[start]))
            {
                start++;
            }

            if (start < text.Length && text[start] == '=')
            {
                start++;
            }

            var tokens = Lexer.Tokenize(text, start);
            var parser = new Parser(tokens, resolveName);
            var root = parser.ParseFormula();
            var canonical = new StringBuilder();
            Print(root, canonical);
            return new CompileResult(new CompiledFormula(root, canonical.ToString(), parser.References), null);
        }
        catch (FormulaParseException ex)
        {
            return Fail(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// Turns canonical <c>[#key]</c> / <c>[@#key]</c> references back into <c>[Display Name]</c> / <c>[@Display Name]</c>
    /// for editing. Keys with no name (e.g. deleted columns) stay as <c>[#key]</c>. Text inside string
    /// literals is never changed. Never throws for any input text.
    /// </summary>
    public static string ToDisplay(string canonical, Func<string, string?> keyToName) =>
        ToDisplay(canonical, keyToName, null);

    /// <summary>
    /// Like <see cref="ToDisplay(string, Func{string, string?})"/>, but when <paramref name="resolveName"/> is given a
    /// reference is only shown by name if that name resolves back to the same key (so duplicate or
    /// ambiguous names fall back to <c>[#key]</c> and re-saving the display text never changes the formula).
    /// </summary>
    public static string ToDisplay(string canonical, Func<string, string?> keyToName, Func<string, string?>? resolveName)
    {
        ArgumentNullException.ThrowIfNull(keyToName);
        if (string.IsNullOrEmpty(canonical))
        {
            return canonical ?? string.Empty;
        }

        var sb = new StringBuilder(canonical.Length + 16);
        var i = 0;
        while (i < canonical.Length)
        {
            var c = canonical[i];
            if (c == '"')
            {
                // Copy a string literal verbatim (including "" escapes); an unterminated one runs to the end.
                var j = i + 1;
                while (j < canonical.Length)
                {
                    if (canonical[j] == '"')
                    {
                        if (j + 1 < canonical.Length && canonical[j + 1] == '"')
                        {
                            j += 2;
                            continue;
                        }

                        j++;
                        break;
                    }

                    j++;
                }

                sb.Append(canonical, i, j - i);
                i = j;
                continue;
            }

            if (c == '[')
            {
                var close = canonical.IndexOf(']', i + 1);
                if (close < 0)
                {
                    sb.Append(canonical, i, canonical.Length - i);
                    break;
                }

                var original = canonical.Substring(i, close - i + 1);
                sb.Append(RenderReference(canonical.Substring(i + 1, close - i - 1), keyToName, resolveName) ?? original);
                i = close + 1;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a case-insensitive, whitespace-trimming display-name resolver from (key, display name) pairs.
    /// A name used by more than one column is ambiguous and resolves to null (<c>#NAME?</c>).
    /// </summary>
    public static Func<string, string?> CreateNameResolver(IEnumerable<KeyValuePair<string, string>> keyToName)
    {
        ArgumentNullException.ThrowIfNull(keyToName);
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, name) in keyToName)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(key))
            {
                continue;
            }

            var trimmed = name.Trim();
            if (map.TryGetValue(trimmed, out var existing))
            {
                if (!string.Equals(existing, key, StringComparison.Ordinal))
                {
                    map[trimmed] = null; // ambiguous
                }
            }
            else
            {
                map[trimmed] = key;
            }
        }

        return name => name is not null && map.TryGetValue(name.Trim(), out var key) ? key : null;
    }

    /// <summary>Whether <paramref name="key"/> can be used in a <c>[#key]</c> reference: 1–100 of A–Z, a–z, 0–9, '_' and '-'.</summary>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
        {
            return false;
        }

        foreach (var c in key)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static string? RenderReference(string body, Func<string, string?> keyToName, Func<string, string?>? resolveName)
    {
        var s = body.Trim();
        var at = string.Empty;
        if (s.StartsWith('@'))
        {
            at = "@";
            s = s[1..].Trim();
        }

        if (!s.StartsWith('#'))
        {
            return null; // already a display-name reference (or garbage): leave untouched
        }

        var key = s[1..].Trim();
        if (!IsValidKey(key))
        {
            return null;
        }

        var name = keyToName(key)?.Trim();
        if (!IsSafeDisplayName(name) || (resolveName is not null && resolveName(name!) != key))
        {
            return $"[{at}#{key}]";
        }

        return $"[{at}{name}]";
    }

    private static bool IsSafeDisplayName(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.IndexOfAny(['[', ']']) < 0
        && name[0] != '#'
        && name[0] != '@';

    private static CompileResult Fail(string code, string message) =>
        new(null, new FormulaValue.Error(code, message));

    private static void Print(Node node, StringBuilder sb)
    {
        switch (node)
        {
            case NumberNode n:
                sb.Append(n.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case TextNode t:
                sb.Append('"').Append(t.Value.Replace("\"", "\"\"")).Append('"');
                break;
            case BoolNode b:
                sb.Append(b.Value ? "TRUE" : "FALSE");
                break;
            case RefNode r:
                sb.Append(r.CurrentRow ? "[@#" : "[#").Append(r.Key).Append(']');
                break;
            case GroupNode g:
                sb.Append('(');
                Print(g.Inner, sb);
                sb.Append(')');
                break;
            case UnaryNode u:
                sb.Append(u.Op);
                Print(u.Operand, sb);
                break;
            case BinaryNode b:
                Print(b.Left, sb);
                sb.Append(' ').Append(BinaryNode.Symbol(b.Op)).Append(' ');
                Print(b.Right, sb);
                break;
            case CallNode c:
                sb.Append(c.Name).Append('(');
                for (var i = 0; i < c.Args.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    Print(c.Args[i], sb);
                }

                sb.Append(')');
                break;
        }
    }
}
