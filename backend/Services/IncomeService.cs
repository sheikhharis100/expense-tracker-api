using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExpenseTracker.Api.Services;

// Salary plans, salary statements (what was earned), and receipts (what actually arrived) with their
// allocations to budget months. Only received receipts are income; statements and plans are expectations.
public class IncomeService
{
    // The salary rule allows a lag of 0–3 months; a statement may be moved further, but not years ahead.
    public const int MaxFundingLag = 12;

    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly LedgerLoader _loader;

    public IncomeService(AppDbContext db, IClock clock, LedgerLoader loader)
    {
        _db = db;
        _clock = clock;
        _loader = loader;
    }

    // ---------- Plans ----------

    public async Task<List<SalaryPlanDto>> PlansAsync(int userId) =>
        (await _db.SalaryPlans.AsNoTracking().Where(p => p.UserId == userId)
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync())
        .Select(ToDto)
        .ToList();

    // A plan change applies from its effective month on; saved statements are never rewritten by it.
    public async Task<ServiceResult<SalaryPlanDto>> UpsertPlanAsync(int userId, SalaryPlanInputDto dto)
    {
        var note = InputRules.Clean(dto.Note);
        var problem = InputRules.Month(dto.EffectiveFrom, "The month the plan starts")
            ?? InputRules.NonNegative(dto.BaseSalary, "Base salary")
            ?? InputRules.NonNegative(dto.Allowances, "Allowances")
            ?? InputRules.Text(note, 200, "Note");
        if (problem != null) return ServiceResult<SalaryPlanDto>.Fail(problem);

        var plan = await _db.SalaryPlans.FirstOrDefaultAsync(p => p.UserId == userId && p.Year == dto.EffectiveFrom.Year && p.Month == dto.EffectiveFrom.Month);
        if (plan == null)
        {
            plan = new SalaryPlan { UserId = userId, Year = dto.EffectiveFrom.Year, Month = dto.EffectiveFrom.Month };
            _db.SalaryPlans.Add(plan);
        }
        else if (dto.Version is uint version && version != plan.Version)
        {
            return StalePlan(plan);
        }
        plan.Amount = dto.BaseSalary;
        plan.Allowances = dto.Allowances;
        plan.Note = note;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Changed between our read and write.
            _db.ChangeTracker.Clear();
            var current = await _db.SalaryPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == plan.Id);
            return current == null ? ServiceResult<SalaryPlanDto>.Fail("Not found", 404) : StalePlan(current);
        }
        return ServiceResult<SalaryPlanDto>.Ok(ToDto(plan));
    }

    private static ServiceResult<SalaryPlanDto> StalePlan(SalaryPlan current) =>
        ServiceResult<SalaryPlanDto>.Fail("This salary plan was changed on another device. Your edit wasn't saved; the latest version is shown.", 409, ToDto(current));

    private static SalaryPlanDto ToDto(SalaryPlan p) => new()
    {
        Id = p.Id,
        Version = p.Version,
        EffectiveFrom = new YearMonth(p.Year, p.Month),
        BaseSalary = p.Amount,
        Allowances = p.Allowances,
        Note = p.Note
    };

    public async Task<bool> DeletePlanAsync(int userId, int id)
    {
        var plan = await _db.SalaryPlans.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (plan == null) return false;
        _db.SalaryPlans.Remove(plan);
        await _db.SaveChangesAsync();
        return true;
    }

    // ---------- Month view ----------

    public async Task<IncomeMonthDto> MonthAsync(int userId, YearMonth month)
    {
        var user = await _loader.UserAsync(userId);
        var data = await _loader.LoadAsync(userId, month, month);
        var figures = LedgerCalculator.Single(data, month);

        var receipts = await _db.IncomeReceipts.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Include(r => r.Allocations)
            .Include(r => r.Category)
            .Include(r => r.SalaryStatement)
            .ToListAsync();

        var funding = new List<SalaryStatementDto>();
        foreach (var s in figures.FundingSalaries)
            funding.Add(await StatementDtoAsync(userId, s.Earned, s, receipts));

        var earnedThisMonth = await StatementForEarnedAsync(user, month, receipts);

        var other = receipts
            .Where(r => r.Kind == ReceiptKind.Other && r.Allocations.Any(a => a.Year == month.Year && a.Month == month.Month))
            .OrderBy(r => r.ReceivedDate ?? r.ExpectedDate)
            .Select(ToDto)
            .ToList();

        return new IncomeMonthDto
        {
            Month = month,
            Currency = user.Currency,
            SalaryLagMonths = user.SalaryLagMonths,
            Funding = funding,
            EarnedThisMonth = earnedThisMonth,
            OtherIncome = other,
            ExpectedSalary = figures.ExpectedSalary,
            SalaryReceived = figures.SalaryReceivedTowardFunding,
            SalaryDue = figures.SalaryOutstanding,
            SalaryOverpaid = figures.SalaryOverpaid,
            SalaryIncome = figures.SalaryIncome,
            OtherIncomeReceived = figures.OtherIncome,
            ExpectedOtherIncome = figures.ExpectedOtherIncome,
            TotalReceivedIncome = figures.ReceivedIncome
        };
    }

    private async Task<SalaryStatementDto?> StatementForEarnedAsync(User user, YearMonth earned, List<IncomeReceipt> receipts)
    {
        var funded = earned.AddMonths(user.SalaryLagMonths);
        var statements = await _loader.StatementsFundingAsync(user, earned.AddMonths(-12), funded.AddMonths(12));
        var s = statements.FirstOrDefault(x => x.Earned == earned);
        if (s == null) return null;
        var figures = Figures(s, receipts);
        return await StatementDtoAsync(user.Id, earned, figures, receipts);
    }

    private static SalaryFigures Figures(LedgerStatement s, List<IncomeReceipt> receipts)
    {
        var expected = SalaryMath.ExpectedNet(s.Components);
        var paid = s.Id is int id ? receipts.Where(r => r.SalaryStatementId == id && r.Status == ReceiptStatus.Received).Sum(r => r.Amount) : 0;
        return new SalaryFigures(s.Id, s.Earned, s.Funded, s.IsPlanned, expected, paid, Math.Max(0, expected - paid),
            Math.Max(0, paid - expected), SalaryMath.Status(expected, paid), s.Components);
    }

    private async Task<SalaryStatementDto> StatementDtoAsync(int userId, YearMonth earned, SalaryFigures f, List<IncomeReceipt> receipts)
    {
        uint version = 0;
        var note = string.Empty;
        if (f.StatementId is int id)
        {
            var row = await _db.SalaryStatements.AsNoTracking().Where(s => s.Id == id && s.UserId == userId).Select(s => new { s.Version, s.Note }).FirstAsync();
            version = row.Version;
            note = row.Note;
        }
        return new SalaryStatementDto
        {
            Id = f.StatementId,
            Version = version,
            Earned = earned,
            Funded = f.Funded,
            IsPlanned = f.IsPlanned,
            Note = note,
            Components = f.Components.Select(c => new SalaryComponentDto { Kind = c.Kind, Label = c.Label, Amount = c.Amount }).ToList(),
            ExpectedNet = f.ExpectedNet,
            Received = f.Received,
            Outstanding = f.Outstanding,
            Overpaid = f.Overpaid,
            Status = f.Status,
            Payments = f.StatementId is int sid ? receipts.Where(r => r.SalaryStatementId == sid).OrderBy(r => r.ReceivedDate).Select(ToDto).ToList() : new()
        };
    }

    public static ReceiptDto ToDto(IncomeReceipt r) => new()
    {
        Id = r.Id,
        Version = r.Version,
        Kind = r.Kind,
        SalaryStatementId = r.SalaryStatementId,
        SalaryEarned = r.SalaryStatement == null ? null : new YearMonth(r.SalaryStatement.EarnedYear, r.SalaryStatement.EarnedMonth),
        CategoryId = r.CategoryId,
        CategoryName = r.Category?.Name ?? (r.Kind == ReceiptKind.Salary ? "Salary" : string.Empty),
        Status = r.Status,
        ReceivedDate = r.ReceivedDate,
        ExpectedDate = r.ExpectedDate,
        Amount = r.Amount,
        Unallocated = r.Amount - r.Allocations.Sum(a => a.Amount),
        Description = r.Description,
        PaymentMethod = r.PaymentMethod,
        Allocations = r.Allocations.OrderBy(a => a.Year).ThenBy(a => a.Month).Select(a => new AllocationDto { Month = new YearMonth(a.Year, a.Month), Amount = a.Amount }).ToList()
    };

    // ---------- Statements ----------

    // Saving a statement freezes it: later salary plan changes no longer affect this month.
    public async Task<ServiceResult<SalaryStatement>> SaveStatementAsync(int userId, YearMonth earned, SalaryStatementInputDto dto)
    {
        foreach (var c in dto.Components)
        {
            if (!SalaryComponentKind.All.Contains(c.Kind)) return ServiceResult<SalaryStatement>.Fail($"Unknown component {c.Kind}.");
            var label = string.IsNullOrWhiteSpace(c.Label) ? c.Kind : c.Label;
            if (InputRules.Signed(c.Amount, label) is { } amountError) return ServiceResult<SalaryStatement>.Fail(amountError);
            if (c.Kind != SalaryComponentKind.Adjustment && c.Amount < 0)
                return ServiceResult<SalaryStatement>.Fail($"{c.Kind} must be zero or more; use Adjustment for signed changes.");
            c.Label = InputRules.Clean(c.Label);
            if (InputRules.Text(c.Label, 100, "Label") is { } labelError) return ServiceResult<SalaryStatement>.Fail(labelError);
        }
        var note = InputRules.Clean(dto.Note);
        var problem = InputRules.Month(dto.Funded, "The month it funds") ?? InputRules.Text(note, InputRules.DescriptionLength, "Note");
        if (problem != null) return ServiceResult<SalaryStatement>.Fail(problem);
        if (dto.Funded < earned) return ServiceResult<SalaryStatement>.Fail("A salary can't fund a month before it was earned.");
        if (dto.Funded > earned.AddMonths(MaxFundingLag))
            return ServiceResult<SalaryStatement>.Fail($"A salary can fund a month at most {MaxFundingLag} months after it was earned.");
        var expected = SalaryMath.ExpectedNet(dto.Components.Select(c => new LedgerComponent(c.Kind, c.Label, c.Amount)).ToList());
        if (expected < 0) return ServiceResult<SalaryStatement>.Fail("Deductions can't be more than the salary; the expected net would be negative.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        var statement = await _db.SalaryStatements.Include(s => s.Components)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.EarnedYear == earned.Year && s.EarnedMonth == earned.Month);
        if (statement == null)
        {
            statement = new SalaryStatement { UserId = userId, EarnedYear = earned.Year, EarnedMonth = earned.Month, CreatedAt = DateTime.UtcNow };
            _db.SalaryStatements.Add(statement);
        }
        else if (dto.Version is uint v)
        {
            _db.Entry(statement).Property(s => s.Version).OriginalValue = v;
        }

        var oldFunded = new YearMonth(statement.FundedYear == 0 ? dto.Funded.Year : statement.FundedYear, statement.FundedMonth == 0 ? dto.Funded.Month : statement.FundedMonth);
        statement.FundedYear = dto.Funded.Year;
        statement.FundedMonth = dto.Funded.Month;
        statement.Note = note;
        _db.SalaryComponents.RemoveRange(statement.Components);
        statement.Components = dto.Components.Select((c, i) => new SalaryComponent
        {
            Kind = c.Kind,
            Label = string.IsNullOrWhiteSpace(c.Label) ? c.Kind : c.Label.Trim(),
            Amount = c.Amount,
            SortOrder = i
        }).ToList();

        // Payments that went wholly to the old funded month follow the statement to its new month.
        if (statement.Id != 0 && oldFunded != dto.Funded)
        {
            var allocations = await _db.IncomeAllocations
                .Where(a => a.Receipt.SalaryStatementId == statement.Id && a.Year == oldFunded.Year && a.Month == oldFunded.Month)
                .ToListAsync();
            foreach (var a in allocations)
            {
                a.Year = dto.Funded.Year;
                a.Month = dto.Funded.Month;
            }
        }

        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<SalaryStatement>.Fail("This salary was changed on another device. Reload and try again.", 409);
        }
        return ServiceResult<SalaryStatement>.Ok(statement);
    }

    // Saves the planned statement as-is if it hasn't been saved yet (so a payment has something to belong to).
    private async Task<SalaryStatement?> EnsureStatementAsync(User user, YearMonth earned)
    {
        var existing = await _db.SalaryStatements.FirstOrDefaultAsync(s => s.UserId == user.Id && s.EarnedYear == earned.Year && s.EarnedMonth == earned.Month);
        if (existing != null) return existing;

        var plans = await _db.SalaryPlans.AsNoTracking().Where(p => p.UserId == user.Id).ToListAsync();
        var funded = earned.AddMonths(user.SalaryLagMonths);
        var planned = LedgerLoader.PlannedStatement(plans, earned, funded);
        var statement = new SalaryStatement
        {
            UserId = user.Id,
            EarnedYear = earned.Year,
            EarnedMonth = earned.Month,
            FundedYear = funded.Year,
            FundedMonth = funded.Month,
            CreatedAt = DateTime.UtcNow,
            Components = (planned?.Components ?? Array.Empty<LedgerComponent>())
                .Select((c, i) => new SalaryComponent { Kind = c.Kind, Label = c.Label, Amount = c.Amount, SortOrder = i })
                .ToList()
        };
        _db.SalaryStatements.Add(statement);
        await _db.SaveChangesAsync();
        return statement;
    }

    // ---------- Receipts ----------

    public async Task<ServiceResult<ReceiptDto>> AddSalaryPaymentAsync(int userId, YearMonth earned, ReceiptInputDto dto)
    {
        var duplicate = await FindByRequestAsync(userId, dto.ClientRequestId);
        if (duplicate != null) return ServiceResult<ReceiptDto>.Ok(duplicate);

        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        await using var tx = await _db.Database.BeginTransactionAsync();
        var statement = await EnsureStatementAsync(user, earned);
        dto.Kind = ReceiptKind.Salary;
        dto.Status = ReceiptStatus.Received;
        dto.DefaultMonth ??= new YearMonth(statement!.FundedYear, statement.FundedMonth);
        var result = await SaveReceiptAsync(user, new IncomeReceipt { UserId = userId, SalaryStatementId = statement!.Id, CreatedAt = DateTime.UtcNow }, dto, isNew: true);
        if (result.Error != null) return result;
        await tx.CommitAsync();
        return result;
    }

    public async Task<ServiceResult<ReceiptDto>> AddOtherIncomeAsync(int userId, ReceiptInputDto dto)
    {
        var duplicate = await FindByRequestAsync(userId, dto.ClientRequestId);
        if (duplicate != null) return ServiceResult<ReceiptDto>.Ok(duplicate);

        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        dto.Kind = ReceiptKind.Other;
        return await SaveReceiptAsync(user, new IncomeReceipt { UserId = userId, CreatedAt = DateTime.UtcNow }, dto, isNew: true);
    }

    public async Task<ServiceResult<ReceiptDto>> UpdateReceiptAsync(int userId, int id, ReceiptInputDto dto)
    {
        var receipt = await _db.IncomeReceipts.Include(r => r.Allocations).FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
        if (receipt == null) return ServiceResult<ReceiptDto>.Fail("Not found", 404);
        if (dto.Version is not uint v) return ServiceResult<ReceiptDto>.Fail("Missing version; reload and try again.");
        _db.Entry(receipt).Property(r => r.Version).OriginalValue = v;
        dto.Kind = receipt.Kind;
        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        return await SaveReceiptAsync(user, receipt, dto, isNew: false);
    }

    public async Task<ServiceResult<bool>> DeleteReceiptAsync(int userId, int id)
    {
        var receipt = await _db.IncomeReceipts.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
        if (receipt == null) return ServiceResult<bool>.Fail("Not found", 404);
        if (receipt.RecurringOccurrenceId is int occurrenceId)
        {
            var occurrence = await _db.RecurringOccurrences.FirstOrDefaultAsync(o => o.Id == occurrenceId && o.UserId == userId);
            if (occurrence != null)
            {
                occurrence.Status = OccurrenceStatus.Planned;
                occurrence.IncomeReceiptId = null;
            }
        }
        _db.IncomeReceipts.Remove(receipt);
        await _db.SaveChangesAsync();
        return ServiceResult<bool>.Ok(true);
    }

    private async Task<ReceiptDto?> FindByRequestAsync(int userId, Guid? requestId)
    {
        if (requestId is not Guid id) return null;
        var existing = await _db.IncomeReceipts.AsNoTracking().Include(r => r.Allocations).Include(r => r.Category).Include(r => r.SalaryStatement)
            .FirstOrDefaultAsync(r => r.UserId == userId && r.ClientRequestId == id);
        return existing == null ? null : ToDto(existing);
    }

    private async Task<ServiceResult<ReceiptDto>> SaveReceiptAsync(User user, IncomeReceipt receipt, ReceiptInputDto dto, bool isNew)
    {
        var today = LocalCalendar.Today(_clock, user.TimeZone);
        var description = InputRules.Clean(dto.Description);
        var problem = InputRules.Amount(dto.Amount)
            ?? InputRules.Date(dto.ReceivedDate, "Received date")
            ?? InputRules.Date(dto.ExpectedDate, "Expected date")
            ?? InputRules.PaymentMethod(dto.PaymentMethod)
            ?? InputRules.Text(description, InputRules.DescriptionLength, "Note")
            ?? (dto.DefaultMonth is YearMonth month ? InputRules.Month(month) : null);
        if (problem != null) return ServiceResult<ReceiptDto>.Fail(problem);

        if (dto.Status == ReceiptStatus.Received)
        {
            if (dto.ReceivedDate is not DateOnly received) return ServiceResult<ReceiptDto>.Fail("Enter the date the money was received.");
            if (received > today) return ServiceResult<ReceiptDto>.Fail($"Received money can't be dated after today ({today:dd MMM yyyy}). Mark it expected instead.");
        }
        else if (receipt.Kind == ReceiptKind.Salary || dto.Kind == ReceiptKind.Salary)
        {
            return ServiceResult<ReceiptDto>.Fail("Salary payments are recorded when received; the expected salary is on the statement.");
        }

        if (dto.Kind == ReceiptKind.Other && dto.CategoryId is int categoryId)
        {
            var category = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId && c.UserId == user.Id);
            if (category == null || category.Type != "Income") return ServiceResult<ReceiptDto>.Fail("Choose an income category.");
            // Archived categories keep their old income but take no new receipts.
            if (category.IsArchived && (isNew || receipt.CategoryId != categoryId))
                return ServiceResult<ReceiptDto>.Fail($"“{category.Name}” is archived; restore it to add income to it.");
        }

        var fallbackMonth = dto.DefaultMonth
            ?? (dto.ReceivedDate is DateOnly rd ? YearMonth.Of(rd) : dto.ExpectedDate is DateOnly ed ? YearMonth.Of(ed) : YearMonth.Of(today));
        var allocations = dto.Allocations is { Count: > 0 } list
            ? list
            : isNew || receipt.Allocations.Count == 0
                ? new List<AllocationDto> { new() { Month = fallbackMonth, Amount = dto.Amount } }
                : ScaleSingle(receipt, dto.Amount);

        if (allocations.Any(a => !Money.IsValidAmount(a.Amount))) return ServiceResult<ReceiptDto>.Fail("Each allocation must be greater than zero.");
        if (allocations.Select(a => InputRules.Month(a.Month, "Each allocation's month")).FirstOrDefault(e => e != null) is { } monthError)
            return ServiceResult<ReceiptDto>.Fail(monthError);
        if (allocations.Select(a => a.Month).Distinct().Count() != allocations.Count) return ServiceResult<ReceiptDto>.Fail("Allocate to each month once.");
        var allocated = allocations.Sum(a => a.Amount);
        if (allocated > dto.Amount)
            return ServiceResult<ReceiptDto>.Fail($"Allocations ({allocated:N2}) can't exceed the amount received ({dto.Amount:N2}).");

        receipt.Kind = dto.Kind;
        receipt.Status = dto.Status;
        receipt.ReceivedDate = dto.Status == ReceiptStatus.Received ? dto.ReceivedDate : null;
        receipt.ExpectedDate = dto.ExpectedDate;
        receipt.Amount = dto.Amount;
        receipt.Description = description;
        receipt.PaymentMethod = dto.PaymentMethod;
        receipt.CategoryId = dto.Kind == ReceiptKind.Other ? dto.CategoryId : null;
        if (isNew) receipt.ClientRequestId = dto.ClientRequestId;

        _db.IncomeAllocations.RemoveRange(receipt.Allocations);
        receipt.Allocations = allocations.Select(a => new IncomeAllocation { Year = a.Month.Year, Month = a.Month.Month, Amount = a.Amount }).ToList();
        if (isNew) _db.IncomeReceipts.Add(receipt);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<ReceiptDto>.Fail("This payment was changed on another device. Reload and try again.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } && dto.ClientRequestId != null)
        {
            _db.ChangeTracker.Clear();
            return ServiceResult<ReceiptDto>.Ok((await FindByRequestAsync(user.Id, dto.ClientRequestId))!);
        }

        var saved = await _db.IncomeReceipts.AsNoTracking().Include(r => r.Allocations).Include(r => r.Category).Include(r => r.SalaryStatement)
            .FirstAsync(r => r.Id == receipt.Id);
        return ServiceResult<ReceiptDto>.Ok(ToDto(saved));
    }

    // Editing the amount of a receipt with one allocation moves that allocation with it.
    private static List<AllocationDto> ScaleSingle(IncomeReceipt receipt, decimal amount) =>
        receipt.Allocations.Count == 1
            ? new List<AllocationDto> { new() { Month = new YearMonth(receipt.Allocations[0].Year, receipt.Allocations[0].Month), Amount = amount } }
            : receipt.Allocations.Select(a => new AllocationDto { Month = new YearMonth(a.Year, a.Month), Amount = a.Amount }).ToList();
}
