using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public static class DefaultData
{
    // Categorical palette, in its validated (colour-blind safe) order. New categories take the next unused slot.
    public static readonly string[] Palette =
    {
        "#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#4a3aa7", "#e34948"
    };

    public const string OverflowColor = "#898781";

    public static readonly (string Name, string? Group)[] ExpenseCategories =
    {
        ("Bill / Rent", null),
        ("Food", null),
        ("Travel — InDrive", "Travel"),
        ("Travel — Bus", "Travel"),
        ("Groceries", null),
        ("Utilities", null),
        ("Medical", null),
        ("Shopping", null),
        ("Subscriptions", null),
        ("Other Expenses", null),
    };

    public static readonly string[] IncomeCategories = { "Freelance", "Gift", "Other income" };

    // Names from the first version that mean the same category, so they aren't added twice.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Travel (InDrive)"] = "Travel — InDrive",
        ["Travel (Bus)"] = "Travel — Bus",
    };

    // True when two names mean the same category, including the old and new spellings of the travel columns.
    public static bool SameCategory(string a, string b)
    {
        static string Canonical(string name) => Aliases.TryGetValue(name.Trim(), out var alias) ? alias : name.Trim();
        return Canonical(a).Equals(Canonical(b), StringComparison.OrdinalIgnoreCase);
    }

    public static string NextColor(IEnumerable<string> usedColors)
    {
        var used = new HashSet<string>(usedColors, StringComparer.OrdinalIgnoreCase);
        return Palette.FirstOrDefault(c => !used.Contains(c)) ?? OverflowColor;
    }

    public static async Task<Account> GetOrCreateDefaultAccountAsync(AppDbContext context, User user)
    {
        if (user.DefaultAccountId is int defaultId)
        {
            var preferred = await context.Accounts.FirstOrDefaultAsync(a => a.Id == defaultId && a.UserId == user.Id);
            if (preferred != null) return preferred;
        }

        var first = await context.Accounts.Where(a => a.UserId == user.Id).OrderBy(a => a.Id).FirstOrDefaultAsync();
        if (first != null) return first;

        var cash = new Account { UserId = user.Id, Name = "Cash", Type = "Cash", Currency = user.Currency };
        context.Accounts.Add(cash);
        await context.SaveChangesAsync();
        var tracked = await context.Users.FirstAsync(u => u.Id == user.Id);
        tracked.DefaultAccountId = cash.Id;
        user.DefaultAccountId = cash.Id;
        await context.SaveChangesAsync();
        return cash;
    }

    // Adds the default account and any starter categories the user is missing. Never renames or removes anything.
    public static async Task<bool> SeedAsync(AppDbContext context, User user)
    {
        var changed = false;
        if (!await context.Accounts.AnyAsync(a => a.UserId == user.Id))
        {
            await GetOrCreateDefaultAccountAsync(context, user);
            changed = true;
        }

        var existing = await context.Categories.Where(c => c.UserId == user.Id).ToListAsync();
        bool Has(string name) => existing.Any(c => SameCategory(c.Name, name));

        void Add(string name, string type, string? group)
        {
            if (Has(name)) return;
            var category = new Category
            {
                UserId = user.Id,
                Name = name,
                Type = type,
                GroupName = group,
                Color = type == "Expense" ? NextColor(existing.Where(c => c.Type == "Expense").Select(c => c.Color)) : "#008300",
                SortOrder = existing.Where(c => c.Type == type).Select(c => c.SortOrder).DefaultIfEmpty(-1).Max() + 1
            };
            context.Categories.Add(category);
            existing.Add(category);
            changed = true;
        }

        foreach (var (name, group) in ExpenseCategories) Add(name, "Expense", group);
        foreach (var name in IncomeCategories) Add(name, "Income", null);

        if (changed) await context.SaveChangesAsync();
        return changed;
    }
}
