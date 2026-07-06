using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _context;

    public CategoriesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<ActionResult<CategoryResponseDto>> CreateCategory(CategoryCreateDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);

        var nameTaken = await _context.Categories
            .AnyAsync(c => c.UserId == userId && c.Name.ToLower() == dto.Name.ToLower());
        if (nameTaken)
            return BadRequest("A category with this name already exists");

        var category = new Category
        {
            UserId = userId,
            Name = dto.Name,
            Type = dto.Type
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCategory), new { id = category.Id }, MapToResponse(category));
    }

    [HttpGet]
    public async Task<ActionResult<List<CategoryResponseDto>>> GetCategories()
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var categories = await _context.Categories
            .Where(c => c.UserId == userId)
            .ToListAsync();

        return Ok(categories.Select(MapToResponse).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CategoryResponseDto>> GetCategory(int id)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

        if (category == null)
            return NotFound();

        return Ok(MapToResponse(category));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCategory(int id, CategoryUpdateDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

        if (category == null)
            return NotFound();

        var nameTaken = await _context.Categories
            .AnyAsync(c => c.UserId == userId && c.Id != id && c.Name.ToLower() == dto.Name.ToLower());
        if (nameTaken)
            return BadRequest("A category with this name already exists");

        category.Name = dto.Name;
        category.Type = dto.Type;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var category = await _context.Categories
            .Include(c => c.Transactions)
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

        if (category == null)
            return NotFound();

        if (category.Transactions.Any())
        {
            return BadRequest("Cannot delete category with existing transactions. Reassign or delete transactions first.");
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static CategoryResponseDto MapToResponse(Category category)
    {
        return new CategoryResponseDto
        {
            Id = category.Id,
            Name = category.Name,
            Type = category.Type
        };
    }
}