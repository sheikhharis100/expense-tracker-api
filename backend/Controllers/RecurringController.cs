using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

[Route("api/recurring")]
public class RecurringController : AppController
{
    private readonly AppDbContext _db;
    private readonly RecurringService _recurring;
    private readonly LedgerNotifier _notifier;

    public RecurringController(AppDbContext db, RecurringService recurring, LedgerNotifier notifier)
    {
        _db = db;
        _recurring = recurring;
        _notifier = notifier;
    }

    // Items active in the month and their planned occurrences (generated on demand, never duplicated).
    [HttpGet("{month}")]
    public async Task<ActionResult<RecurringMonthDto>> Month(string month)
    {
        if (!YearMonth.TryParse(month, out var ym)) return BadMonth();
        await _recurring.EnsureOccurrencesAsync(UserId, ym, ym);

        var items = await _db.RecurringItems.AsNoTracking().Include(r => r.Category)
            .Where(r => r.UserId == UserId)
            .OrderBy(r => r.Description)
            .ToListAsync();
        var occurrences = await _db.RecurringOccurrences.AsNoTracking().Include(o => o.RecurringItem).ThenInclude(r => r.Category)
            .Where(o => o.UserId == UserId && o.Year == ym.Year && o.Month == ym.Month)
            .OrderBy(o => o.DueDate)
            .ToListAsync();

        return Ok(new RecurringMonthDto
        {
            Month = ym,
            // The version of each series that applies to this month, or its latest version otherwise.
            Items = items.GroupBy(i => i.SeriesId)
                .Select(g => g.FirstOrDefault(i => RecurringService.Covers(i, ym)) ?? g.OrderByDescending(i => i.StartYear * 12 + i.StartMonth).First())
                .Select(ToDto).ToList(),
            Occurrences = occurrences.Select(ToDto).ToList(),
            PlannedUnpaid = occurrences.Where(o => o.Status == OccurrenceStatus.Planned && o.RecurringItem.Kind == TxKind.Expense).Sum(o => o.Amount)
        });
    }

    [HttpPost]
    public async Task<ActionResult> Create(RecurringInputDto dto)
    {
        var error = await ValidateAsync(dto);
        if (error != null) return Error(error);
        var item = new RecurringItem
        {
            UserId = UserId,
            SeriesId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        Apply(item, dto);
        _db.RecurringItems.Add(item);
        await _db.SaveChangesAsync();
        await _notifier.NotifyAsync(UserId, LedgerScopes.Recurring);
        return Ok(ToDto(item));
    }

    // Changes apply to `from` and later months only; earlier months and paid occurrences keep their values.
    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, RecurringInputDto dto, [FromQuery] string from)
    {
        if (!YearMonth.TryParse(from, out var fromMonth)) return BadMonth();
        var item = await _db.RecurringItems.FirstOrDefaultAsync(r => r.Id == id && r.UserId == UserId);
        if (item == null) return NotFound(new { error = "Not found" });
        var error = await ValidateAsync(dto);
        if (error != null) return Error(error);
        if (dto.Version is uint v) _db.Entry(item).Property(r => r.Version).OriginalValue = v;

        try
        {
            var updated = await _recurring.ChangeFromAsync(item, fromMonth, target =>
            {
                Apply(target, dto);
                if (target.Id == 0 || new YearMonth(target.StartYear, target.StartMonth) > fromMonth)
                {
                    target.StartYear = fromMonth.Year;
                    target.StartMonth = fromMonth.Month;
                }
            });
            await _notifier.NotifyAsync(UserId, LedgerScopes.Recurring);
            return Ok(ToDto(updated));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "This item was changed on another device. Reload and try again." });
        }
    }

    // Stops the series from `from` on. Paid occurrences (and their transactions) are kept.
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Stop(int id, [FromQuery] string from)
    {
        if (!YearMonth.TryParse(from, out var fromMonth)) return BadMonth();
        var item = await _db.RecurringItems.FirstOrDefaultAsync(r => r.Id == id && r.UserId == UserId);
        if (item == null) return NotFound(new { error = "Not found" });

        await _recurring.ChangeFromAsync(item, fromMonth, target => target.IsActive = false);
        var hasPaid = await _db.RecurringOccurrences.AnyAsync(o => o.SeriesId == item.SeriesId && o.Status != OccurrenceStatus.Planned);
        var versions = await _db.RecurringItems.Where(r => r.SeriesId == item.SeriesId && !r.IsActive).ToListAsync();
        if (!hasPaid && versions.All(v => new YearMonth(v.StartYear, v.StartMonth) >= fromMonth))
        {
            _db.RecurringOccurrences.RemoveRange(_db.RecurringOccurrences.Where(o => o.SeriesId == item.SeriesId));
            _db.RecurringItems.RemoveRange(versions);
            await _db.SaveChangesAsync();
        }
        await _notifier.NotifyAsync(UserId, LedgerScopes.Recurring);
        return NoContent();
    }

    // This occurrence only.
    [HttpPut("occurrences/{id:int}")]
    public async Task<ActionResult> UpdateOccurrence(int id, OccurrenceUpdateDto dto)
    {
        var o = await _db.RecurringOccurrences.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (o == null) return NotFound(new { error = "Not found" });
        if (o.Status == OccurrenceStatus.Paid) return Error("This occurrence is paid; edit the linked transaction instead.");
        if (!Money.IsValidAmount(dto.Amount)) return Error("Amount must be greater than zero with at most 2 decimals.");
        if (YearMonth.Of(dto.DueDate) != new YearMonth(o.Year, o.Month)) return Error("The due date must stay in the same month.");

        _db.Entry(o).Property(x => x.Version).OriginalValue = dto.Version;
        o.Amount = dto.Amount;
        o.DueDate = dto.DueDate;
        o.Note = dto.Note.Trim();
        o.IsOverridden = true;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "This occurrence was changed on another device. Reload and try again." });
        }
        await _notifier.NotifyAsync(UserId, LedgerScopes.Recurring, new YearMonth(o.Year, o.Month));
        return Ok(ToDto(await Load(id)));
    }

    [HttpPost("occurrences/{id:int}/pay")]
    public async Task<ActionResult> Pay(int id, PayOccurrenceDto dto)
    {
        var result = await _recurring.PayAsync(UserId, id, dto.Date, dto.Amount, dto.ClientRequestId);
        if (result.Error == "Not found") return NotFound(new { error = result.Error });
        if (result.Error != null && result.Occurrence == null && !result.AlreadyPaid) return Error(result.Error);
        await _notifier.NotifyAsync(UserId, LedgerScopes.Entries, new YearMonth(result.Occurrence!.Year, result.Occurrence.Month));
        return Ok(new { alreadyPaid = result.AlreadyPaid, occurrence = ToDto(await Load(id)) });
    }

    [HttpPost("occurrences/{id:int}/unpay")]
    public async Task<ActionResult> Unpay(int id)
    {
        var error = await _recurring.UnpayAsync(UserId, id);
        if (error == "Not found") return NotFound(new { error });
        if (error != null) return Error(error);
        var o = await Load(id);
        await _notifier.NotifyAsync(UserId, LedgerScopes.Entries, new YearMonth(o.Year, o.Month));
        return Ok(ToDto(o));
    }

    [HttpPost("occurrences/{id:int}/skip")]
    public Task<ActionResult> Skip(int id) => SetStatus(id, OccurrenceStatus.Skipped);

    [HttpPost("occurrences/{id:int}/unskip")]
    public Task<ActionResult> Unskip(int id) => SetStatus(id, OccurrenceStatus.Planned);

    private async Task<ActionResult> SetStatus(int id, string status)
    {
        var o = await _db.RecurringOccurrences.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (o == null) return NotFound(new { error = "Not found" });
        if (o.Status == OccurrenceStatus.Paid) return Error("This occurrence is paid; unpay it first.");
        o.Status = status;
        await _db.SaveChangesAsync();
        await _notifier.NotifyAsync(UserId, LedgerScopes.Recurring, new YearMonth(o.Year, o.Month));
        return Ok(ToDto(await Load(id)));
    }

    private async Task<RecurringOccurrence> Load(int id) =>
        await _db.RecurringOccurrences.AsNoTracking().Include(o => o.RecurringItem).ThenInclude(r => r.Category).FirstAsync(o => o.Id == id);

    private async Task<string?> ValidateAsync(RecurringInputDto dto)
    {
        if (!Money.IsValidAmount(dto.Amount)) return "Amount must be greater than zero with at most 2 decimals.";
        if (dto.End is YearMonth end && end < dto.Start) return "The end month is before the start month.";
        if (dto.PaymentMethod.Length > 0 && !PaymentMethods.All.Contains(dto.PaymentMethod)) return "Unknown payment method.";
        var wantType = dto.Kind == "Income" ? "Income" : "Expense";
        if (dto.CategoryId is int cid)
        {
            var category = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid && c.UserId == UserId);
            if (category == null || category.Type != wantType) return $"Choose a {(wantType == "Income" ? "income" : "spending")} category.";
        }
        else if (wantType == "Expense")
        {
            return "Choose a category.";
        }
        return null;
    }

    private static void Apply(RecurringItem item, RecurringInputDto dto)
    {
        item.Kind = dto.Kind == "Income" ? "Income" : TxKind.Expense;
        item.Description = dto.Description.Trim();
        item.CategoryId = dto.CategoryId;
        item.Amount = dto.Amount;
        item.DayOfMonth = dto.DayOfMonth;
        item.PaymentMethod = dto.PaymentMethod;
        item.StartYear = dto.Start.Year;
        item.StartMonth = dto.Start.Month;
        item.EndYear = dto.End?.Year;
        item.EndMonth = dto.End?.Month;
        item.IsActive = true;
    }

    private static RecurringItemDto ToDto(RecurringItem r) => new()
    {
        Id = r.Id,
        SeriesId = r.SeriesId,
        Kind = r.Kind,
        Description = r.Description,
        CategoryId = r.CategoryId,
        CategoryName = r.Category?.Name ?? string.Empty,
        Amount = r.Amount,
        DayOfMonth = r.DayOfMonth,
        PaymentMethod = r.PaymentMethod,
        Start = new YearMonth(r.StartYear, r.StartMonth),
        End = r.EndYear is int y ? new YearMonth(y, r.EndMonth ?? 12) : null,
        IsActive = r.IsActive,
        Version = r.Version
    };

    private static OccurrenceDto ToDto(RecurringOccurrence o) => new()
    {
        Id = o.Id,
        Version = o.Version,
        RecurringItemId = o.RecurringItemId,
        SeriesId = o.SeriesId,
        Kind = o.RecurringItem.Kind,
        Description = o.RecurringItem.Description,
        CategoryId = o.RecurringItem.CategoryId,
        CategoryName = o.RecurringItem.Category?.Name ?? string.Empty,
        DueDate = o.DueDate,
        Amount = o.Amount,
        Status = o.Status,
        IsOverridden = o.IsOverridden,
        TransactionId = o.TransactionId,
        IncomeReceiptId = o.IncomeReceiptId,
        Note = o.Note
    };
}
