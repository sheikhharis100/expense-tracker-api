# Calculated columns and formulas

A calculated column adds a column to the expense ledger whose value comes from a formula, such as a tip, a tax share or a "% of month" figure. The formula runs once for each row, which means once for each transaction in the selected month.

Calculated columns are display-only. They never change your transactions, balances, budgets or income and expense totals. If a formula is wrong, only that column shows the wrong value or an error.

> **Not Excel.** The syntax looks like a spreadsheet's, but only the small subset described on this page is supported. Anything not listed here (other functions, cell addresses like `A1`, ranges like `A1:B5`, `^`, `%`, wildcards, array formulas) is rejected with an error. It is never guessed at.

## Writing a formula

- You can start a formula with `=` or leave it out. `=[Amount] * 0.1` and `[Amount] * 0.1` are the same formula.
- Formulas can be up to **1000 characters** long. Parentheses, functions and +/- signs can be nested up to **64 levels** deep.
- Spaces and line breaks between parts of a formula are ignored.

### Values

| Kind    | Examples                       | Notes |
|---------|--------------------------------|-------|
| Number  | `12`, `12.5`, `.5`             | No thousands separators (`1,000` is not allowed). Arithmetic is exact decimal: `0.1 + 0.2` is exactly `0.3`. |
| Text    | `"Food"`, `"Say ""hi"""`       | Use double quotes. Write `""` inside text for a literal quote. |
| Boolean | `TRUE`, `FALSE`                | Upper or lower case. |
| Date    | `"2026-11-05"`                 | Dates are text in `yyyy-MM-dd` form, so they compare correctly with `<`, `>` and the rest. There is no date arithmetic. |

### Column references

| You write          | Meaning |
|--------------------|---------|
| `[Amount]`         | The column named "Amount". Names are not case-sensitive, and spaces just inside the brackets are ignored. |
| `[@Amount]`        | The value of Amount **in the current row**, wherever the reference appears. |
| `[#amount]`        | The column whose internal key is `amount`. |
| `[@#c12]`          | The current row's value of the column with key `c12`. |

The app saves formulas with internal keys (`[#c12]`) and shows them to you with names (`[Tip %]`). This means:

- **Renaming or reordering a column never breaks a formula.** The formula picks up the new name automatically.
- **Deleting a column** that a formula uses makes that formula show `#REF!`.
- A column whose name contains `[` or `]`, or starts with `#` or `@`, or shares its name with another column, is shown as `[#key]` in the formula editor.

### Current row or whole column?

A plain reference such as `[Amount]` means **this row's value**. The one exception is when the reference is written **directly** as an argument to `SUM`, `AVERAGE`, `MIN`, `MAX` or `COUNT`, or as a range argument of `SUMIF`/`SUMIFS`. In those places it means **the whole column**: every row in the table.

| Formula                         | Result on each row |
|---------------------------------|--------------------|
| `[Amount] * 0.1`                | 10% of this row's amount |
| `SUM([Amount])`                 | The month's total (the same on every row) |
| `[Amount] / SUM([Amount])`      | This row's share of the total |
| `SUM([@Amount])`                | Just this row's amount (`@` forces the current row) |
| `SUM([Amount] * 2)`             | This row's amount × 2. The argument is an expression, not a plain reference, so it uses the current row. |

A calculated column can use another calculated column. Circular references are not allowed: a column cannot depend on itself, directly or through other columns. The app refuses to save a formula that would create a loop, and any loop that does occur shows `#CIRC!`.

## Operators

From highest to lowest precedence. Use parentheses to change the order.

| Operator                         | Meaning | Example | Result |
|----------------------------------|---------|---------|--------|
| `-x`, `+x`                       | Negate / plus sign | `-[Amount]` | |
| `*`, `/`                         | Multiply, divide | `2 + 3 * 4` | `14` |
| `+`, `-`                         | Add, subtract | `(2 + 3) * 4` | `20` |
| `&`                              | Join text | `[Category] & ": " & [Amount]` | `Food: 12.5` |
| `=`, `<>`, `<`, `>`, `<=`, `>=`  | Compare (gives TRUE/FALSE) | `[Amount] > 100` | `TRUE` |

How values are compared:

- If both sides are numbers, they are compared as numbers. This includes TRUE/FALSE (counted as 1/0) and text that looks like a number, such as `"100"`.
- Otherwise both sides are compared as text, ignoring case. `"food" = "Food"` is TRUE, and dates such as `[Date] >= "2026-11-01"` compare correctly.

## Functions

Function names are not case-sensitive. Any other function name gives `#NAME?`.

| Function | What it does | Example |
|----------|--------------|---------|
| `SUM(value, ...)` | Adds numbers. | `SUM([Amount])` gives the month total |
| `AVERAGE(value, ...)` | Average of the numbers. `#DIV/0!` if there are none. | `AVERAGE([Amount])` |
| `MIN(value, ...)` | Smallest number. **0 if there are no numbers.** | `MIN([Amount])` |
| `MAX(value, ...)` | Largest number. **0 if there are no numbers.** | `MAX([Amount], 0)` |
| `COUNT(value, ...)` | How many numbers. | `COUNT([Amount])` |
| `IF(condition, then, [else])` | `then` if the condition is true, otherwise `else` (or FALSE if `else` is left out). Only the chosen branch is calculated. | `IF([Qty] = 0, 0, [Amount] / [Qty])` never gives `#DIV/0!` |
| `ROUND(number, digits)` | Rounds halves away from zero. `digits` must be a whole number from -10 to 10. Negative digits round to tens, hundreds and so on. | `ROUND(2.5, 0)` gives `3`, `ROUND(-2.5, 0)` gives `-3`, `ROUND(1.005, 2)` gives `1.01`, `ROUND(1234, -2)` gives `1200` |
| `ABS(number)` | Absolute value. | `ABS(-5)` gives `5` |
| `SUMIF(range, criteria, [sum_range])` | Adds `sum_range` (or `range` itself) on rows where `range` matches `criteria`. | `SUMIF([Category], "Food", [Amount])` |
| `SUMIFS(sum_range, range1, criteria1, range2, criteria2, ...)` | Adds `sum_range` on rows where **every** range matches its criteria. | `SUMIFS([Amount], [Category], "Food", [Date], ">=2026-11-01")` |

`SUMIF`/`SUMIFS` ranges must be whole-column references like `[Category]`. Expressions and `[@...]` are not allowed there.

### Criteria (SUMIF / SUMIFS)

| Criteria | Matches |
|----------|---------|
| `"Food"` | Text equal to Food, ignoring case |
| `100` or `"100"` | The number 100, including text cells that hold "100" |
| `TRUE` | TRUE cells |
| `">100"`, `"<=50"` | Numbers greater than 100 / at most 50. Cells that aren't numbers never match a numeric criterion. |
| `"<>Food"` | Anything that isn't Food. **Blank cells don't match.** |
| `">=2026-11-10"` | Dates on or after 10 Nov 2026 (compared as text) |
| `""` or `"="` | Blank (and empty) cells only |
| `"<>"` | Any cell that isn't blank or empty |
| `[Category]` | The current row's category, e.g. "total spent in this row's category" |

Wildcards (`*`, `?`) are not supported.

## Blanks

- In arithmetic and in comparisons with numbers, a blank cell counts as `0`. With text, it counts as empty text `""`.
- `SUM`, `AVERAGE`, `MIN`, `MAX` and `COUNT` **skip** blank cells. They also skip text and TRUE/FALSE cells when reading a column. Values you type directly into the function work differently: `SUM("5", TRUE)` is 6, while `SUM("abc")` is `#VALUE!`.
- A formula that just returns a blank cell shows an empty cell.

## Errors

An error is never silently turned into 0 or a blank. If any value a formula uses is an error, the formula shows that error too, including an error anywhere in a column passed to `SUM`. In `SUMIF`/`SUMIFS`, an error in a criteria column always counts. An error in the summed column only counts on rows that match.

| Error | Cause |
|-------|-------|
| `#DIV/0!` | Division by zero (a blank cell counts as 0), or `AVERAGE` with no numbers |
| `#VALUE!` | Text that isn't a number used in arithmetic (`"abc" * 2`), an `IF` condition that isn't TRUE/FALSE or a number, a bad `ROUND` digits value, a range argument that isn't a column, text over 32,767 characters, or formula columns nested more than 256 deep |
| `#REF!` | The formula uses a column that no longer exists (e.g. it was deleted) |
| `#NAME?` | An unknown function, or a `[Column Name]` that doesn't exist |
| `#CIRC!` | A circular reference between calculated columns |
| `#NUM!` | A number too large to represent (above about 7.9 × 10²⁸) |
| `#SYNTAX!` | The formula can't be read, e.g. `Expected ')' at position 14`. Positions count characters from 1, including a leading `=`. |

## Examples

| Goal | Formula |
|------|---------|
| 10% tip | `ROUND([Amount] * 0.1, 2)` |
| Tip from a "Tip %" column | `ROUND([Amount] * [Tip %] / 100, 2)` |
| Share of the month | `ROUND([Amount] / SUM([Amount]) * 100, 1)` |
| Flag big expenses | `IF([Amount] > 100, "Big", "")` |
| Category total on every row | `SUMIF([Category], [Category], [Amount])` |
| Food spending in the first half of the month | `SUMIFS([Amount], [Category], "Food", [Date], "<2026-11-16")` |
| Label | `[Category] & " - " & [Description]` |
