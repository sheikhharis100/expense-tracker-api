using System.Runtime.CompilerServices;
using ExpenseTracker.Formulas.Evaluation;
using ExpenseTracker.Formulas.Syntax;

namespace ExpenseTracker.Formulas;

/// <summary>
/// A table of <see cref="RowCount"/> rows made of base (non-formula) columns and formula columns.
/// Formula cells are evaluated lazily, memoised, and protected against circular references (<c>#CIRC!</c>).
/// Not thread-safe: create one sheet per request/table.
/// </summary>
public sealed class FormulaSheet
{
    /// <summary>Maximum chain of formula columns depending on formula columns that is evaluated.</summary>
    public const int MaxChainDepth = 256;

    private const byte NotStarted = 0;
    private const byte InProgress = 1;
    private const byte Done = 2;

    private readonly IReadOnlyDictionary<string, Func<int, FormulaValue>> _baseColumns;
    private readonly IReadOnlyDictionary<string, CompiledFormula> _formulaColumns;
    private readonly Dictionary<string, FormulaValue?[]> _baseCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (byte[] State, FormulaValue?[] Values)> _formulaCache = new(StringComparer.Ordinal);
    private readonly Dictionary<Node, FormulaValue> _aggregateCache = new(ReferenceEqualityComparer.Instance);
    private int _depth;

    /// <param name="rowCount">Number of rows (e.g. transactions in the selected month).</param>
    /// <param name="baseColumns">Base column key → function returning the cell value for a row index (null is treated as blank).</param>
    /// <param name="formulaColumns">Formula column key → compiled formula.</param>
    /// <exception cref="ArgumentException">A key is both a base and a formula column, or a formula is null.</exception>
    public FormulaSheet(
        int rowCount,
        IReadOnlyDictionary<string, Func<int, FormulaValue>> baseColumns,
        IReadOnlyDictionary<string, CompiledFormula> formulaColumns)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentNullException.ThrowIfNull(baseColumns);
        ArgumentNullException.ThrowIfNull(formulaColumns);

        foreach (var (key, formula) in formulaColumns)
        {
            if (formula is null)
            {
                throw new ArgumentException($"Formula column '{key}' has no compiled formula.", nameof(formulaColumns));
            }

            if (baseColumns.ContainsKey(key))
            {
                throw new ArgumentException($"Column '{key}' is both a base column and a formula column.", nameof(formulaColumns));
            }
        }

        RowCount = rowCount;
        _baseColumns = baseColumns;
        _formulaColumns = formulaColumns;
    }

    /// <summary>Number of rows in the table.</summary>
    public int RowCount { get; }

    /// <summary>Whether <paramref name="key"/> is a base or formula column of this sheet.</summary>
    public bool HasColumn(string key) =>
        key is not null && (_formulaColumns.ContainsKey(key) || _baseColumns.ContainsKey(key));

    /// <summary>
    /// Gets the value of column <paramref name="key"/> on row <paramref name="row"/> (0-based), evaluating
    /// formula columns (and the formula columns they depend on) on demand. Returns <c>#REF!</c> for an
    /// unknown key or a row outside the table and <c>#CIRC!</c> for circular references; never throws
    /// because of formula content.
    /// </summary>
    public FormulaValue Get(string key, int row)
    {
        if (key is null)
        {
            return ValueOps.Error(FormulaErrors.Ref, "No column key given.");
        }

        if (_formulaColumns.TryGetValue(key, out var formula))
        {
            if ((uint)row >= (uint)RowCount)
            {
                return RowOutOfRange(row);
            }

            if (!_formulaCache.TryGetValue(key, out var slot))
            {
                slot = (new byte[RowCount], new FormulaValue?[RowCount]);
                _formulaCache[key] = slot;
            }

            switch (slot.State[row])
            {
                case Done:
                    return slot.Values[row]!;
                case InProgress:
                    return ValueOps.Error(FormulaErrors.Circular,
                        $"Circular reference: column [#{key}] depends on itself.");
            }

            if (_depth >= MaxChainDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                return ValueOps.Error(FormulaErrors.Value, "Too many formula columns depend on each other.");
            }

            slot.State[row] = InProgress;
            _depth++;
            FormulaValue result;
            try
            {
                result = new Evaluator(this, row).Evaluate(formula.Root) ?? FormulaValue.Blank.Instance;
            }
            catch
            {
                slot.State[row] = NotStarted;
                throw;
            }
            finally
            {
                _depth--;
            }

            slot.Values[row] = result;
            slot.State[row] = Done;
            return result;
        }

        if (_baseColumns.TryGetValue(key, out var getter))
        {
            if ((uint)row >= (uint)RowCount)
            {
                return RowOutOfRange(row);
            }

            if (!_baseCache.TryGetValue(key, out var values))
            {
                values = new FormulaValue?[RowCount];
                _baseCache[key] = values;
            }

            return values[row] ??= getter(row) ?? FormulaValue.Blank.Instance;
        }

        return MissingColumn(key);
    }

    internal FormulaValue.Error MissingColumn(string key) =>
        ValueOps.Error(FormulaErrors.Ref, $"Column [#{key}] does not exist (it may have been deleted).");

    internal bool TryGetCached(Node node, out FormulaValue value) =>
        _aggregateCache.TryGetValue(node, out value!);

    internal void StoreCached(Node node, FormulaValue value) => _aggregateCache[node] = value;

    private static FormulaValue.Error RowOutOfRange(int row) =>
        ValueOps.Error(FormulaErrors.Ref, $"Row {row} is outside the table.");
}
