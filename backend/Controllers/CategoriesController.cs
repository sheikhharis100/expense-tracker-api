using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

// Categories are the sheet's columns: expense columns in the Daily Log, income lines in the month view.
[ApiController]
[Route("api/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly LedgerNotifier _notifier;

    public CategoriesController(AppDbContext context, LedgerNotifier notifier)
    {
        _context = context;
        _notifier = notifier;
    }

    [HttpPost]
    public async Task<ActionResult<CategoryResponseDto>> CreateCategory(CategoryCreateDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var name = dto.Name.Trim();

        var existing = await _context.Categories.Where(c => c.UserId == userId).ToListAsync();
        if (existing.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return BadRequest("A category with this name already exists");

        var category = new Category
        {
            UserId = userId,
            Name = name,
            Type = dto.Type,
            Color = dto.Color ?? DefaultData.NextColor(existing.Select(c => c.Color)),
            GroupName = NormalizeGroup(dto.GroupName),
            SortOrder = existing.Where(c => c.Type == dto.Type).Select(c => c.SortOrder).DefaultIfEmpty(-1).Max() + 1
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        await _notifier.NotifyAsync(userId, LedgerScopes.Categories);
        return CreatedAtAction(nameof(GetCategory), new { id = category.Id }, MapToResponse(category, 0));
    }

    [HttpGet]
    public async Task<ActionResult<List<CategoryResponseDto>>> GetCategories()
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var categories = await _context.Categories
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Type).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { Category = c, Count = c.Transactions.Count })
            .ToListAsync();

        return Ok(categories.Select(x => MapToResponse(x.Category, x.Count)).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CategoryResponseDto>> GetCategory(int id)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var found = await _context.Categories
            .Where(c => c.Id == id && c.UserId == userId)
            .Select(c => new { Category = c, Count = c.Transactions.Count })
            .FirstOrDefaultAsync();

        if (found == null)
            return NotFound();

        return Ok(MapToResponse(found.Category, found.Count));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCategory(int id, CategoryUpdateDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

        if (category == null)
            return NotFound();

        var name = dto.Name.Trim();
        var nameTaken = await _context.Categories
            .AnyAsync(c => c.UserId == userId && c.Id != id && c.Name.ToLower() == name.ToLower());
        if (nameTaken)
            return BadRequest("A category with this name already exists");

        if (category.Type != dto.Type && await _context.Transactions.AnyAsync(t => t.CategoryId == id))
            return BadRequest("Can't switch between income and expense once a category has entries");

        category.Name = name;
        category.Type = dto.Type;
        category.Color = dto.Color ?? category.Color;
        category.GroupName = NormalizeGroup(dto.GroupName);
        category.IsArchived = dto.IsArchived;

        await _context.SaveChangesAsync();
        await _notifier.NotifyAsync(userId, LedgerScopes.Categories);
        return NoContent();
    }

    // Column order in the Daily Log and month views.
    [HttpPut("order")]
    public async Task<IActionResult> Reorder(CategoryReorderDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var categories = await _context.Categories.Where(c => c.UserId == userId).ToListAsync();
        var byId = categories.ToDictionary(c => c.Id);

        if (dto.OrderedIds.Distinct().Count() != dto.OrderedIds.Count || dto.OrderedIds.Any(id => !byId.ContainsKey(id)))
            return BadRequest("Order must list each of your categories at most once");

        for (var i = 0; i < dto.OrderedIds.Count; i++)
            byId[dto.OrderedIds[i]].SortOrder = i;

        await _context.SaveChangesAsync();
        await _notifier.NotifyAsync(userId, LedgerScopes.Categories);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

        if (category == null)
            return NotFound();

        if (await _context.Transactions.AnyAsync(t => t.CategoryId == id)
            || await _context.IncomeReceipts.AnyAsync(r => r.CategoryId == id)
            || await _context.RecurringItems.AnyAsync(r => r.CategoryId == id))
            return BadRequest("This category has entries. Archive it instead to hide it from new months.");

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        await _notifier.NotifyAsync(userId, LedgerScopes.Categories);
        return NoContent();
    }

    private static string? NormalizeGroup(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();

    private static CategoryResponseDto MapToResponse(Category category, int transactionCount)
    {
        return new CategoryResponseDto
        {
            Id = category.Id,
            Name = category.Name,
            Type = category.Type,
            Color = category.Color,
            SortOrder = category.SortOrder,
            GroupName = category.GroupName,
            IsArchived = category.IsArchived,
            TransactionCount = transactionCount
        };
    }
}
