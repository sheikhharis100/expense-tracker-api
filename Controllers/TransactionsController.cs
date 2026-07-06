using System.Globalization;
using CsvHelper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/transactions")]
[Authorize]
public class TransactionsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly CsvImportService _csvService;
    private readonly AnalyticsService _analyticsService;

    public TransactionsController(AppDbContext context, CsvImportService csvService, AnalyticsService analyticsService)
    {
        _context = context;
        _csvService = csvService;
        _analyticsService = analyticsService;
    }

    [HttpPost]
    public async Task<ActionResult<TransactionResponseDto>> CreateTransaction(TransactionCreateDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == dto.AccountId && a.UserId == userId);
        if (account == null)
            return BadRequest("Invalid account");

        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == dto.CategoryId && c.UserId == userId);
        if (category == null)
            return BadRequest("Invalid category");

        var transaction = new Transaction
        {
            AccountId = dto.AccountId,
            CategoryId = dto.CategoryId,
            Amount = dto.Amount,
            Description = dto.Description,
            TransactionDate = dto.TransactionDate,
            CreatedAt = DateTime.UtcNow
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        await _analyticsService.CheckBudgetAlertAsync(userId, transaction.CategoryId, transaction.TransactionDate.Month, transaction.TransactionDate.Year);

        return CreatedAtAction(nameof(GetTransaction), new { id = transaction.Id }, MapToResponse(transaction, account.Name, category.Name));
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<TransactionResponseDto>>> GetTransactions([FromQuery] TransactionFilterDto filter)
    {
        var userId = AuthHelper.GetCurrentUserId(User);

        var query = _context.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category)
            .Where(t => t.Account.UserId == userId);

        if (filter.AccountId.HasValue)
            query = query.Where(t => t.AccountId == filter.AccountId.Value);

        if (filter.CategoryId.HasValue)
            query = query.Where(t => t.CategoryId == filter.CategoryId.Value);

        if (filter.FromDate.HasValue)
            query = query.Where(t => t.TransactionDate >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(t => t.TransactionDate <= filter.ToDate.Value);

        var totalCount = await query.CountAsync();

        var transactions = await query
            .OrderByDescending(t => t.TransactionDate)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        var results = transactions.Select(t => MapToResponse(t, t.Account.Name, t.Category.Name)).ToList();

        return Ok(new PagedResult<TransactionResponseDto>
        {
            Items = results,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TransactionResponseDto>> GetTransaction(int id)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var transaction = await _context.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null)
            return NotFound();

        return Ok(MapToResponse(transaction, transaction.Account.Name, transaction.Category.Name));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTransaction(int id, TransactionUpdateDto dto)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var transaction = await _context.Transactions
            .Include(t => t.Account)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null)
            return NotFound();

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == dto.AccountId && a.UserId == userId);
        if (account == null)
            return BadRequest("Invalid account");

        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == dto.CategoryId && c.UserId == userId);
        if (category == null)
            return BadRequest("Invalid category");

        transaction.AccountId = dto.AccountId;
        transaction.CategoryId = dto.CategoryId;
        transaction.Amount = dto.Amount;
        transaction.Description = dto.Description;
        transaction.TransactionDate = dto.TransactionDate;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTransaction(int id)
    {
        var userId = AuthHelper.GetCurrentUserId(User);
        var transaction = await _context.Transactions
            .Include(t => t.Account)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null)
            return NotFound();

        _context.Transactions.Remove(transaction);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("import")]
    public async Task<ActionResult<CsvImportResponseDto>> ImportTransactions(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        var userId = AuthHelper.GetCurrentUserId(User);

        using var stream = file.OpenReadStream();
        var result = await _csvService.ImportTransactionsAsync(userId, stream);

        return Ok(new CsvImportResponseDto
        {
            TotalRows = result.TotalRows,
            ImportedSuccessfully = result.ImportedSuccessfully,
            Skipped = result.Skipped,
            Errors = result.Errors.Select(e => new CsvImportErrorDto { Row = e.Row, Reason = e.Reason }).ToList()
        });
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportTransactions([FromQuery] TransactionFilterDto filter)
    {
        var userId = AuthHelper.GetCurrentUserId(User);

        var query = _context.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category)
            .Where(t => t.Account.UserId == userId);

        if (filter.AccountId.HasValue)
            query = query.Where(t => t.AccountId == filter.AccountId.Value);

        if (filter.CategoryId.HasValue)
            query = query.Where(t => t.CategoryId == filter.CategoryId.Value);

        if (filter.FromDate.HasValue)
            query = query.Where(t => t.TransactionDate >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(t => t.TransactionDate <= filter.ToDate.Value);

        var transactions = await query.OrderByDescending(t => t.TransactionDate).ToListAsync();

        using var memoryStream = new MemoryStream();
        using (var writer = new StreamWriter(memoryStream, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteField("Date");
            csv.WriteField("Description");
            csv.WriteField("Amount");
            csv.WriteField("Category");
            csv.WriteField("Account");
            csv.NextRecord();

            foreach (var t in transactions)
            {
                csv.WriteField(t.TransactionDate.ToString("yyyy-MM-dd"));
                csv.WriteField(t.Description);
                csv.WriteField(t.Amount.ToString(CultureInfo.InvariantCulture));
                csv.WriteField(t.Category.Name);
                csv.WriteField(t.Account.Name);
                csv.NextRecord();
            }
        }

        return File(memoryStream.ToArray(), "text/csv", "transactions.csv");
    }

    private static TransactionResponseDto MapToResponse(Transaction transaction, string accountName, string categoryName)
    {
        return new TransactionResponseDto
        {
            Id = transaction.Id,
            AccountId = transaction.AccountId,
            AccountName = accountName,
            CategoryId = transaction.CategoryId,
            CategoryName = categoryName,
            Amount = transaction.Amount,
            Description = transaction.Description,
            TransactionDate = transaction.TransactionDate,
            CreatedAt = transaction.CreatedAt
        };
    }
}