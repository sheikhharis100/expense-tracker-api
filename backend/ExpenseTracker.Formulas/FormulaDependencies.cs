namespace ExpenseTracker.Formulas;

/// <summary>Static analysis of dependencies between formula columns.</summary>
public static class FormulaDependencies
{
    /// <summary>
    /// Finds one circular dependency between formula columns and returns it as a closed path of keys,
    /// e.g. <c>["c3", "c5", "c3"]</c> (a self-reference gives <c>["c3", "c3"]</c>), or null when there is none.
    /// References to keys that are not formula columns (base columns, deleted columns) are ignored.
    /// The search order is deterministic (keys are visited in ordinal order).
    /// </summary>
    /// <param name="formulaRefs">Formula column key → the keys its formula references (<see cref="CompiledFormula.References"/>).</param>
    public static IReadOnlyList<string>? FindCycle(IReadOnlyDictionary<string, IReadOnlySet<string>> formulaRefs)
    {
        if (formulaRefs is null || formulaRefs.Count == 0)
        {
            return null;
        }

        const byte onStack = 1;
        const byte finished = 2;
        var state = new Dictionary<string, byte>(StringComparer.Ordinal);
        var path = new List<string>();
        var stack = new Stack<(string Key, IEnumerator<string> Next)>();

        foreach (var start in formulaRefs.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (state.ContainsKey(start))
            {
                continue;
            }

            Push(start);
            while (stack.Count > 0)
            {
                var (key, next) = stack.Peek();
                if (!next.MoveNext())
                {
                    stack.Pop();
                    path.RemoveAt(path.Count - 1);
                    state[key] = finished;
                    continue;
                }

                var dependency = next.Current;
                if (dependency is null || !formulaRefs.ContainsKey(dependency))
                {
                    continue;
                }

                if (state.TryGetValue(dependency, out var s))
                {
                    if (s == onStack)
                    {
                        var cycle = path.GetRange(path.IndexOf(dependency), path.Count - path.IndexOf(dependency));
                        cycle.Add(dependency);
                        return cycle;
                    }

                    continue;
                }

                Push(dependency);
            }
        }

        return null;

        void Push(string key)
        {
            state[key] = onStack;
            path.Add(key);
            IEnumerable<string> refs = formulaRefs.TryGetValue(key, out var r) && r is not null ? r : Array.Empty<string>();
            stack.Push((key, refs.OrderBy(x => x, StringComparer.Ordinal).GetEnumerator()));
        }
    }

    /// <summary>Convenience overload: finds a cycle among compiled formula columns.</summary>
    public static IReadOnlyList<string>? FindCycle(IReadOnlyDictionary<string, CompiledFormula> formulas)
    {
        ArgumentNullException.ThrowIfNull(formulas);
        return FindCycle(formulas.ToDictionary(
            kv => kv.Key,
            kv => kv.Value?.References ?? (IReadOnlySet<string>)new HashSet<string>()));
    }
}
