using System.Globalization;
using System.Text.Json;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Formulas;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

// Built-in and custom ledger columns, calculated columns, and per-month / template table layouts.
// Formulas are stored with stable [#key] references, so renaming or reordering never breaks them.
public class ColumnService
{
    public const string ExpensesTable = "expenses";
    public const string DailyTable = "daily";

    public sealed record ColumnInfo(ColumnDto Dto, CustomColumn? Custom, CompiledFormula? Formula);

    // Built-in fields. Essential ones (date, amount, type, category) stay available to the backend even when hidden.
    private static readonly (string Key, string Name, string Type, bool Essential)[] BuiltIns =
    {
        ("date", "Date", "date", true),
        ("category", "Category", "dropdown", true),
        ("description", "Description", "text", false),
        ("amount", "Amount", "money", true),
        ("kind", "Type", "dropdown", true),
        ("status", "Status", "dropdown", false),
        ("method", "Payment method", "dropdown", false),
        ("additional", "Additional", "checkbox", false),
        ("recurring", "Recurring", "checkbox", false),
    };

    private readonly AppDbContext _db;

    public ColumnService(AppDbContext db)
    {
        _db = db;
    }

    // ---------- Definitions ----------

    public async Task<List<ColumnInfo>> ColumnsAsync(int userId, bool includeArchived = true)
    {
        var custom = await _db.CustomColumns.AsNoTracking()
            .Where(c => c.UserId == userId && c.TableKey == ExpensesTable)
            .OrderBy(c => c.Id)
            .ToListAsync();

        var names = BuiltIns.Select(b => new KeyValuePair<string, string>(b.Key, b.Name))
            .Concat(custom.Select(c => new KeyValuePair<string, string>(c.Key, c.Name)))
            .ToList();
        var keyToName = names.ToDictionary(n => n.Key, n => n.Value);

        var result = BuiltIns.Select(b => new ColumnInfo(new ColumnDto
        {
            Key = b.Key,
            Name = b.Name,
            Type = b.Type,
            BuiltIn = true,
            Essential = b.Essential,
            Editable = b.Key != "recurring",
            Decimals = b.Type == "money" ? 2 : 0,
            Options = b.Key switch
            {
                "kind" => TxKind.Editable.ToList(),
                "status" => new() { TxStatus.Paid, TxStatus.Planned },
                "method" => PaymentMethods.All.ToList(),
                _ => new()
            }
        }, null, null)).ToList();

        foreach (var c in custom.Where(c => includeArchived || !c.IsArchived))
        {
            CompiledFormula? formula = null;
            string? formulaError = null;
            string? display = null;
            if (c.DataType == "formula")
            {
                var compiled = FormulaCompiler.Compile(c.Formula, _ => null);
                formula = compiled.Formula;
                formulaError = compiled.Error is { } e ? $"{e.Code} {e.Message}" : null;
                display = FormulaCompiler.ToDisplay(c.Formula, key => keyToName.GetValueOrDefault(key));
            }

            result.Add(new ColumnInfo(new ColumnDto
            {
                Key = c.Key,
                Id = c.Id,
                Name = c.Name,
                Type = c.DataType,
                BuiltIn = false,
                Essential = false,
                Editable = c.DataType != "formula",
                Options = CustomValues.Options(c),
                Formula = display,
                FormulaError = formulaError,
                Decimals = c.Decimals,
                IsArchived = c.IsArchived,
                Version = c.Version
            }, c, formula));
        }
        return result;
    }

    private static Func<string, string?> Resolver(IEnumerable<ColumnInfo> columns) =>
        FormulaCompiler.CreateNameResolver(columns.Select(c => new KeyValuePair<string, string>(c.Dto.Key, c.Dto.Name)));

    public async Task<FormulaCheckResultDto> CheckFormulaAsync(int userId, string text, int? columnId)
    {
        var columns = await ColumnsAsync(userId);
        var compiled = FormulaCompiler.Compile(text, Resolver(columns));
        if (compiled.Formula is not { } formula)
            return new FormulaCheckResultDto { Ok = false, Error = $"{compiled.Error!.Code} {compiled.Error.Message}" };

        var missing = formula.References.FirstOrDefault(k => columns.All(c => c.Dto.Key != k));
        if (missing != null)
            return new FormulaCheckResultDto { Ok = false, Error = $"#REF! There's no column [#{missing}]" };

        var selfKey = columns.FirstOrDefault(c => c.Dto.Id == columnId)?.Dto.Key ?? "__new__";
        if (formula.References.Contains(selfKey))
            return new FormulaCheckResultDto { Ok = false, Error = "#CIRC! A column can't refer to itself" };
        var cycle = FindCycle(columns, selfKey, formula);
        if (cycle != null)
            return new FormulaCheckResultDto { Ok = false, Error = $"#CIRC! Circular reference: {string.Join(" → ", cycle.Select(k => NameOf(columns, k)))}" };

        var keyToName = columns.ToDictionary(c => c.Dto.Key, c => c.Dto.Name);
        return new FormulaCheckResultDto
        {
            Ok = true,
            Canonical = formula.Canonical,
            Display = FormulaCompiler.ToDisplay(formula.Canonical, k => keyToName.GetValueOrDefault(k)),
            References = formula.References.ToList()
        };
    }

    private static string NameOf(IEnumerable<ColumnInfo> columns, string key) =>
        columns.FirstOrDefault(c => c.Dto.Key == key)?.Dto.Name ?? (key == "__new__" ? "this column" : key);

    private static IReadOnlyList<string>? FindCycle(List<ColumnInfo> columns, string selfKey, CompiledFormula formula)
    {
        var graph = columns.Where(c => c.Formula != null && c.Dto.Key != selfKey)
            .ToDictionary(c => c.Dto.Key, c => (IReadOnlySet<string>)c.Formula!.References);
        graph[selfKey] = formula.References;
        return FormulaDependencies.FindCycle(graph);
    }

    public async Task<ServiceResult<ColumnDto>> CreateAsync(int userId, ColumnInputDto dto)
    {
        var error = await ValidateAsync(userId, dto, null);
        if (error.Error != null) return ServiceResult<ColumnDto>.Fail(error.Error);

        var column = new CustomColumn
        {
            UserId = userId,
            TableKey = ExpensesTable,
            Key = "pending",
            Name = dto.Name.Trim(),
            DataType = dto.Type,
            OptionsJson = JsonSerializer.Serialize(CleanOptions(dto.Options)),
            Formula = error.Canonical ?? string.Empty,
            Decimals = dto.Decimals,
            CreatedAt = DateTime.UtcNow
        };
        await using var tx = await _db.Database.BeginTransactionAsync();
        _db.CustomColumns.Add(column);
        await _db.SaveChangesAsync();
        // The key is derived from the id once, then never changes.
        column.Key = $"c{column.Id}";
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ServiceResult<ColumnDto>.Ok((await ColumnsAsync(userId)).First(c => c.Dto.Id == column.Id).Dto);
    }

    public async Task<ServiceResult<ColumnDto>> UpdateAsync(int userId, int id, ColumnInputDto dto)
    {
        var column = await _db.CustomColumns.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (column == null) return ServiceResult<ColumnDto>.Fail("Not found", 404);

        var check = await ValidateAsync(userId, dto, column);
        if (check.Error != null) return ServiceResult<ColumnDto>.Fail(check.Error);

        if (dto.Version is uint v) _db.Entry(column).Property(c => c.Version).OriginalValue = v;
        column.Name = dto.Name.Trim();
        column.DataType = dto.Type;
        column.OptionsJson = JsonSerializer.Serialize(CleanOptions(dto.Options));
        column.Formula = check.Canonical ?? string.Empty;
        column.Decimals = dto.Decimals;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<ColumnDto>.Fail("This column was changed on another device. Reload and try again.", 409);
        }
        return ServiceResult<ColumnDto>.Ok((await ColumnsAsync(userId)).First(c => c.Dto.Id == id).Dto);
    }

    private async Task<(string? Error, string? Canonical)> ValidateAsync(int userId, ColumnInputDto dto, CustomColumn? existing)
    {
        var columns = await ColumnsAsync(userId);
        var name = dto.Name.Trim();
        if (name.Length == 0) return ("Give the column a name.", null);
        if (columns.Any(c => c.Dto.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && c.Dto.Id != existing?.Id))
            return ($"There's already a column called “{name}”.", null);
        if (name.Contains('[') || name.Contains(']')) return ("Column names can't contain [ or ].", null);
        if (dto.Type == "dropdown" && CleanOptions(dto.Options).Count == 0) return ("A dropdown needs at least one option.", null);
        if (existing != null && existing.DataType == "formula" != (dto.Type == "formula"))
            return ("A calculated column can't be turned into a typed column (or back). Add a new column instead.", null);

        if (dto.Type != "formula") return (null, null);
        if (string.IsNullOrWhiteSpace(dto.Formula)) return ("Enter a formula, e.g. [Amount] * 0.1", null);
        var check = await CheckFormulaAsync(userId, dto.Formula, existing?.Id);
        return check.Ok ? (null, check.Canonical) : (check.Error, null);
    }

    private static List<string> CleanOptions(IEnumerable<string> options) =>
        options.Select(o => o.Trim()).Where(o => o.Length > 0 && o.Length <= 60).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();

    // Removing a column never happens silently: with values or dependent formulas the caller must pick
    // archive (hide, keep values) or delete (remove values; dependent formulas show #REF!).
    public async Task<ServiceResult<bool>> RemoveAsync(int userId, int id, string? mode)
    {
        var column = await _db.CustomColumns.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (column == null) return ServiceResult<bool>.Fail("Not found", 404);

        var columns = await ColumnsAsync(userId);
        var dependents = columns.Where(c => c.Formula?.References.Contains(column.Key) == true).Select(c => c.Dto.Name).ToList();
        var rows = await _db.Transactions
            .Where(t => t.UserId == userId && EF.Functions.JsonExists(t.CustomValuesJson, column.Key))
            .ToListAsync();

        if (mode == null && (rows.Count > 0 || dependents.Count > 0))
            return ServiceResult<bool>.Fail("This column has data.", 409,
                new ColumnRemovalImpactDto { Error = "This column has data.", ValueCount = rows.Count, DependentColumns = dependents });

        if (mode == "archive")
        {
            column.IsArchived = true;
            await _db.SaveChangesAsync();
            return ServiceResult<bool>.Ok(true);
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        foreach (var t in rows)
        {
            var values = CustomValues.Read(t.CustomValuesJson);
            values.Remove(column.Key);
            t.CustomValuesJson = CustomValues.Write(values);
        }
        _db.CustomColumns.Remove(column);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<bool>> RestoreAsync(int userId, int id)
    {
        var column = await _db.CustomColumns.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (column == null) return ServiceResult<bool>.Fail("Not found", 404);
        column.IsArchived = false;
        await _db.SaveChangesAsync();
        return ServiceResult<bool>.Ok(true);
    }

    // ---------- Values ----------

    public async Task<(string? Json, string? Error)> NormalizeValuesAsync(int userId, Dictionary<string, JsonElement> values)
    {
        var columns = await _db.CustomColumns.AsNoTracking()
            .Where(c => c.UserId == userId && c.TableKey == ExpensesTable)
            .ToDictionaryAsync(c => c.Key);
        var result = new Dictionary<string, JsonElement>();
        foreach (var (key, raw) in values)
        {
            if (!columns.TryGetValue(key, out var column))
                return (null, $"Unknown column “{key}”.");
            if (column.DataType == "formula") continue;
            var (value, error) = CustomValues.Normalize(column, raw);
            if (error != null) return (null, error);
            if (value is JsonElement v) result[key] = v;
        }
        return (CustomValues.Write(result), null);
    }

    // Evaluates calculated columns for every row. Errors are shown as errors, never turned into zero.
    public void Compute(List<ColumnInfo> columns, List<ExpenseRowDto> rows)
    {
        var formulas = columns.Where(c => c.Formula != null).ToDictionary(c => c.Dto.Key, c => c.Formula!);
        var broken = columns.Where(c => c.Dto.Type == "formula" && c.Formula == null).ToList();
        if (formulas.Count == 0 && broken.Count == 0) return;

        var typed = columns.Where(c => c.Custom != null && c.Dto.Type != "formula").ToList();
        var baseColumns = new Dictionary<string, Func<int, FormulaValue>>
        {
            ["date"] = i => new FormulaValue.Text(rows[i].Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ["category"] = i => FormulaValue.FromText(rows[i].CategoryName),
            ["description"] = i => FormulaValue.FromText(rows[i].Description),
            // Signed like the totals: refunds reduce spending.
            ["amount"] = i => new FormulaValue.Number(rows[i].Kind == TxKind.Refund ? -rows[i].Amount : rows[i].Amount),
            ["kind"] = i => new FormulaValue.Text(rows[i].Kind),
            ["status"] = i => new FormulaValue.Text(rows[i].Status),
            ["method"] = i => FormulaValue.FromText(rows[i].PaymentMethod),
            ["additional"] = i => FormulaValue.Bool.Of(rows[i].IsAdditional),
            ["recurring"] = i => FormulaValue.Bool.Of(rows[i].IsRecurring),
        };
        foreach (var c in typed)
        {
            var key = c.Dto.Key;
            var type = c.Dto.Type;
            baseColumns[key] = i => ToFormulaValue(rows[i].Custom.TryGetValue(key, out var v) ? v : null, type);
        }

        var sheet = new FormulaSheet(rows.Count, baseColumns, formulas);
        for (var i = 0; i < rows.Count; i++)
        {
            foreach (var (key, _) in formulas)
            {
                var value = sheet.Get(key, i);
                rows[i].Computed[key] = value switch
                {
                    FormulaValue.Error e => new CellValueDto { Error = e.Code, Display = $"{e.Code} {e.Message}".Trim() },
                    FormulaValue.Number n => new CellValueDto { Value = JsonSerializer.SerializeToElement(n.Value), Display = n.Value.ToString(CultureInfo.InvariantCulture) },
                    FormulaValue.Bool b => new CellValueDto { Value = JsonSerializer.SerializeToElement(b.Value), Display = b.Value ? "TRUE" : "FALSE" },
                    FormulaValue.Text t => new CellValueDto { Value = JsonSerializer.SerializeToElement(t.Value), Display = t.Value },
                    _ => new CellValueDto { Display = string.Empty }
                };
            }
            foreach (var c in broken)
                rows[i].Computed[c.Dto.Key] = new CellValueDto { Error = "#SYNTAX!", Display = c.Dto.FormulaError ?? "#SYNTAX!" };
        }
    }

    private static FormulaValue ToFormulaValue(JsonElement? value, string type)
    {
        if (value is not JsonElement v || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return FormulaValue.Blank.Instance;
        return type switch
        {
            "money" or "number" => CustomValues.TryNumber(v, out var n) ? new FormulaValue.Number(n) : new FormulaValue.Error(FormulaErrors.Value, "Not a number"),
            "checkbox" => v.ValueKind is JsonValueKind.True or JsonValueKind.False ? FormulaValue.Bool.Of(v.GetBoolean()) : FormulaValue.Blank.Instance,
            _ => FormulaValue.FromText(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())
        };
    }

    // ---------- Layouts ----------

    public async Task<LayoutDto> LayoutAsync(int userId, string table, YearMonth month)
    {
        var monthRow = await _db.TableLayouts.AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserId == userId && l.TableKey == table && l.Scope == "month" && l.Year == month.Year && l.Month == month.Month);
        TableLayout? source = monthRow;
        var sourceName = monthRow != null ? "month" : "default";
        if (source == null)
        {
            source = await _db.TableLayouts.AsNoTracking()
                .Where(l => l.UserId == userId && l.TableKey == table && l.Scope == "template" && l.Year * 12 + l.Month - 1 <= month.Index)
                .OrderByDescending(l => l.Year * 12 + l.Month)
                .FirstOrDefaultAsync();
            if (source != null) sourceName = "template";
        }

        var saved = source == null ? new List<LayoutColumnDto>() : JsonSerializer.Deserialize<List<LayoutColumnDto>>(source.ColumnsJson, JsonOptions) ?? new();
        var known = await KnownKeysAsync(userId, table);
        // Keep saved order for known columns, then append columns added since.
        var columns = saved.Where(c => known.Contains(c.Key)).ToList();
        columns.AddRange(known.Where(k => columns.All(c => c.Key != k)).Select(k => new LayoutColumnDto { Key = k }));

        return new LayoutDto
        {
            Table = table,
            Source = sourceName,
            SourceMonth = source == null ? null : new YearMonth(source.Year, source.Month),
            Columns = columns
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<List<string>> KnownKeysAsync(int userId, string table)
    {
        if (table == DailyTable)
            return await _db.Categories.Where(c => c.UserId == userId && c.Type == "Expense")
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(c => "cat:" + c.Id).ToListAsync();

        var custom = await _db.CustomColumns.Where(c => c.UserId == userId && c.TableKey == ExpensesTable && !c.IsArchived)
            .OrderBy(c => c.Id).Select(c => c.Key).ToListAsync();
        return BuiltIns.Select(b => b.Key).Concat(custom).ToList();
    }

    public async Task<ServiceResult<LayoutDto>> SaveLayoutAsync(int userId, string table, YearMonth month, LayoutUpdateDto dto)
    {
        if (table is not (ExpensesTable or DailyTable)) return ServiceResult<LayoutDto>.Fail("Unknown table", 404);
        var known = (await KnownKeysAsync(userId, table)).ToHashSet();
        var unknown = dto.Columns.FirstOrDefault(c => !known.Contains(c.Key));
        if (unknown != null) return ServiceResult<LayoutDto>.Fail($"Unknown column “{unknown.Key}”.");
        if (dto.Columns.Select(c => c.Key).Distinct().Count() != dto.Columns.Count) return ServiceResult<LayoutDto>.Fail("Each column may appear once.");

        var json = JsonSerializer.Serialize(dto.Columns, JsonOptions);
        await using var tx = await _db.Database.BeginTransactionAsync();
        if (dto.Scope == "month")
        {
            await Upsert(userId, table, "month", month, json);
        }
        else
        {
            await Upsert(userId, table, "template", month, json);
            // "This and future months" replaces later one-month layouts, and this month's own override.
            var overrides = await _db.TableLayouts
                .Where(l => l.UserId == userId && l.TableKey == table && l.Scope == "month" && l.Year * 12 + l.Month - 1 >= month.Index)
                .ToListAsync();
            _db.TableLayouts.RemoveRange(overrides);
            var laterTemplates = await _db.TableLayouts
                .Where(l => l.UserId == userId && l.TableKey == table && l.Scope == "template" && l.Year * 12 + l.Month - 1 > month.Index)
                .ToListAsync();
            _db.TableLayouts.RemoveRange(laterTemplates);
        }
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ServiceResult<LayoutDto>.Ok(await LayoutAsync(userId, table, month));
    }

    private async Task Upsert(int userId, string table, string scope, YearMonth month, string json)
    {
        var row = await _db.TableLayouts.FirstOrDefaultAsync(l =>
            l.UserId == userId && l.TableKey == table && l.Scope == scope && l.Year == month.Year && l.Month == month.Month);
        if (row == null)
        {
            row = new TableLayout { UserId = userId, TableKey = table, Scope = scope, Year = month.Year, Month = month.Month };
            _db.TableLayouts.Add(row);
        }
        row.ColumnsJson = json;
        row.UpdatedAt = DateTime.UtcNow;
    }

    // Copies the layout in effect for `from` onto `to` as a one-month layout.
    public async Task<int> CopyLayoutsAsync(int userId, YearMonth from, YearMonth to)
    {
        var copied = 0;
        foreach (var table in new[] { ExpensesTable, DailyTable })
        {
            var layout = await LayoutAsync(userId, table, from);
            if (layout.Source == "default") continue;
            await Upsert(userId, table, "month", to, JsonSerializer.Serialize(layout.Columns, JsonOptions));
            copied++;
        }
        await _db.SaveChangesAsync();
        return copied;
    }
}
