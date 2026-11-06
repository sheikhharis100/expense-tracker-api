# Expense Tracker

A monthly income, salary and expense manager that grew out of a Google Sheet. Every month has its own workspace; the
backend calculation engine is the single source of every figure on screen, in reports and in exports. Changes sync
live between devices (SignalR), and the app is built to be used on a phone every day.

## What's in a month

Pick a year and one of its 12 month tabs (past, current and future months are marked differently). Each month has:

- **Summary**: remaining balance and a health read-out, the month statement (expected / received / outstanding salary,
  other income, opening balance, available funds, gross spending, refunds, net spending, additional expenses shown as
  already included, surplus/deficit), budget vs actual, the daily allowance with its assumptions, planned items kept
  apart from actuals, top categories, a comparison with last month, charts, and a separate cash-flow (receipt-date) view.
- **Salary & Income**: the salary that funds this month (earned month → funded month), its components (base, allowance,
  bonus, overtime, arrears, deduction, signed adjustment), payments toward it with allocations to budget months, and
  other income that is expected or received.
- **Expenses / Daily log**: a dates × categories matrix and a spreadsheet ledger over the same transactions. A cell with
  several entries opens them individually. The ledger has search, filters, sorting, filtered totals shown apart from the
  month totals, custom columns (text, money, number, date, dropdown, checkbox, calculated) with per-month or template
  layouts, duplicate, refund and delete.
- **Additional expenses**: the same ledger filtered to entries flagged additional/unexpected (counted once).
- **Budgets & Recurring**: budgets per category for this month only or this and future months, recurring items with
  planned occurrences (pay, skip, edit this occurrence, change this and future), opening balance, reserved savings, and
  "copy setup from another month".

Plus an **annual summary** (one row per month), **Settings** (time zone, salary rule, carryover, salary plan,
categories, accounts, Google Sheet import) and **CSV/XLSX exports** stamped with the reporting month and accounting basis.

## Rules the engine follows

All money is `System.Decimal` stored as `numeric(18,2)`; rounding is explicit (`Ledger/Money.cs`). "Today" and month
boundaries use the user's zone, **Asia/Karachi** by default (`Ledger/LocalCalendar.cs`). The calculator lives in
`backend/Ledger/LedgerCalculator.cs`; `LedgerLoader` only reads data for it.

| Figure | Rule |
| --- | --- |
| Expected net salary | base + allowances + bonuses + overtime + arrears + signed adjustments − deductions |
| Salary outstanding | expected net − received payments linked to that salary (overpayment shown separately) |
| Received income (budget month) | received salary + received other income **allocated** to the month |
| Gross spending | paid expenses dated in the month; refunds = paid refunds dated in the month; net = gross − refunds |
| Available funds | opening balance + received income (opening balance is never income) |
| Remaining balance | available funds − net spending; surplus = received income − net spending |
| Budgets | category budget − category net; overall = sum of set budgets − all net spending; "Not set" when none |
| Income used | net spending ÷ received income × 100, "N/A" when income is zero |
| Average per day | net ÷ elapsed days (whole month past, today's day current, "N/A" future) |
| Daily allowance | max(0, remaining − unpaid planned − reserved savings) ÷ days left including today (current month) |
| Carryover (optional) | each month opens with the previous closing balance, deficits included; computed on read, so backdated edits flow forward |

Never counted: planned expenses, expected income, salary plans/statements without receipts, deductions as expenses,
transfers between your own accounts, custom/calculated column values. The default salary rule is "October salary funds
November" (configurable 0–3 months), including December → January.

Calculated columns use a small, safe formula language (not Excel): see [docs/formulas.md](docs/formulas.md).

## Run locally

Prerequisites: .NET 8 SDK, Node 20+, Docker.

```bash
docker compose up -d db                        # Postgres on localhost:5433

cd backend                                     # once: local secrets
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5433;Database=expensetracker;Username=expense;Password=expense"
dotnet user-secrets set "Jwt:Key" "<any random string of 32+ characters>"
dotnet run --launch-profile http               # API on :5216; applies migrations and the one-time backfill

cd ../frontend
npm install
npm run dev -- --host                          # app on :5173, also reachable from your phone on the same Wi-Fi
```

### On your phone

- Same Wi-Fi as the laptop: open `http://<laptop-ip>:5173` (Windows may ask to allow Node through the firewall).
- Add it to the home screen (Share → Add to Home Screen on iPhone, ⋮ → Install app on Android); it opens full screen.
- To use it anywhere, deploy it (below) and open your Render URL instead.

## Upgrading existing data

`LedgerV2` (EF migration) adds the new tables and columns and moves data in SQL: each transaction gets its owner from
its account, category budgets become budget templates effective from 2000-01, old recurring transactions become
recurring series starting at their next due month, and budget alerts get a category and month.

`LegacyBackfillService` then runs once per user at startup (inside a database transaction, tracked by
`Users.DataVersion`), with these assumptions:

1. The first version treated the planned salary as received in every month up to today. Those months get a saved salary
   statement and one received payment dated the 1st of the budget month, so past totals don't change. Future months
   stay expected.
2. A month's "salary adjust" becomes an Adjustment line on that month's salary (and part of its payment unless the
   month is in the future); its note moves to the month's settings.
3. Entries in income categories become received other income on the same date. The original rows are kept, marked
   `LegacyIncome`, and excluded from all totals.
4. Missing starter categories (Groceries, Utilities, Medical, Shopping, Subscriptions…) are added; nothing is renamed.

## Tests

```bash
dotnet test                 # backend: calculator, salary, calendar, sheet import, formula engine,
                            #   and API integration tests against a throwaway Postgres container (needs Docker)
cd frontend && npm test     # cell parsing and the health read-out
```

## Run it for free

Everything in this project costs nothing to use:

| Piece | What | Cost |
| --- | --- | --- |
| Code | .NET 8, ASP.NET Core, EF Core, Npgsql, React, Vite, SignalR, ClosedXML, CsvHelper… | Free, open-source licences (MIT / Apache-2.0 / PostgreSQL / MS-PL; font OFL) |
| Database | [Neon](https://neon.com) Free plan | $0, no card. Never bills overages; at its limit it pauses until next month and keeps the data |
| App hosting | [Render](https://render.com) Free web service | $0. Sleeps after 15 min idle and takes up to a minute to wake |
| Code hosting | GitHub (public or private repo) | $0 |

Don't use Render's own free Postgres for this: it is deleted 30 days after it's created.

### Deploy (about 10 minutes)

1. **Neon** → sign up → *New project* (region: *AWS Asia Pacific (Singapore)* is closest to Pakistan) → *Connect* →
   copy the connection string with **Connection pooling turned off**. It looks like
   `postgresql://user:password@ep-….ap-southeast-1.aws.neon.tech/neondb?sslmode=require`.
2. **GitHub**: push this repository.
3. **Render** → *New* → **Web Service** (not *Blueprint*: Blueprints ask for a card) → pick the repository.
   Language **Docker**, region **Singapore**, instance type **Free**, and under *Advanced* set the health check path to
   `/healthz`. Add two environment variables:
   - `ConnectionStrings__DefaultConnection` = the Neon string from step 1
   - `Jwt__Key` = any random text of 40+ characters (it signs your logins; keep it secret)

   Everything else has defaults in `appsettings.json`. Create the service; the first build takes a few minutes, and
   migrations run by themselves on startup.
4. Open `https://<your-service>.onrender.com` on your phone, sign up, and choose *Add to Home Screen*.

**About the wake-up wait.** A free Render service sleeps after 15 minutes without visits and takes up to a minute to
wake. You can keep it awake with a free UptimeRobot or cron-job.org monitor on `https://<your-service>.onrender.com/healthz`
every 10 minutes, but Render's 750 free hours are shared by **all free services in a workspace**: an always-on service
uses up to 744 of them and can leave your other free services suspended for the rest of the month. Only do this in a
workspace where this is the only free service. `/healthz` never touches the database, so pings don't use Neon's free compute.

`render.yaml` describes the same setup if you ever prefer a Blueprint.

### Free local development

- .NET SDK, Node and VS Code are free. The C# Dev Kit extension is free for personal use and teams of up to 5; otherwise
  use the free, open-source *C#* extension on its own.
- Docker Desktop is free for personal use and companies under 250 people and $10M revenue. If that doesn't apply, use
  [Podman Desktop](https://podman-desktop.io) (free) and run `podman compose up -d db`, or skip a local database and put a
  Neon **branch** connection string in `dotnet user-secrets`. The integration tests need Docker or Podman.
