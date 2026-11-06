using Microsoft.EntityFrameworkCore;
using ExpenseTracker.Api.Models;

namespace ExpenseTracker.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Account> Accounts { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<Budget> Budgets { get; set; }
    public DbSet<BudgetTemplate> BudgetTemplates { get; set; }
    public DbSet<BudgetAlert> BudgetAlerts { get; set; }
    public DbSet<RecurringItem> RecurringItems { get; set; }
    public DbSet<RecurringOccurrence> RecurringOccurrences { get; set; }
    public DbSet<SalaryPlan> SalaryPlans { get; set; }
    public DbSet<SalaryStatement> SalaryStatements { get; set; }
    public DbSet<SalaryComponent> SalaryComponents { get; set; }
    public DbSet<IncomeReceipt> IncomeReceipts { get; set; }
    public DbSet<IncomeAllocation> IncomeAllocations { get; set; }
    public DbSet<MonthSetting> MonthSettings { get; set; }
    public DbSet<CustomColumn> CustomColumns { get; set; }
    public DbSet<TableLayout> TableLayouts { get; set; }
    public DbSet<DayNote> DayNotes { get; set; }
    public DbSet<MonthAdjustment> MonthAdjustments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        const string money = "decimal(18,2)";

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.TimeZone).HasDefaultValue("Asia/Karachi");
        });

        modelBuilder.Entity<Account>(e =>
        {
            e.Property(a => a.OpeningBalance).HasColumnType(money);
            e.HasOne(a => a.User).WithMany(u => u.Accounts).HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.HasOne(c => c.User).WithMany(u => u.Categories).HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.Property(t => t.Amount).HasColumnType(money);
            e.Property(t => t.Kind).HasMaxLength(20).HasDefaultValue(TxKind.Expense);
            e.Property(t => t.Status).HasMaxLength(20).HasDefaultValue(TxStatus.Paid);
            e.Property(t => t.PaymentMethod).HasMaxLength(40).HasDefaultValue("");
            e.Property(t => t.CustomValuesJson).HasColumnType("jsonb").HasDefaultValue("{}");
            e.Property(t => t.Version).IsRowVersion();
            e.HasIndex(t => t.TransactionDate);
            e.HasIndex(t => new { t.UserId, t.TransactionDate });
            e.HasIndex(t => new { t.UserId, t.ClientRequestId }).IsUnique().HasFilter("\"ClientRequestId\" IS NOT NULL");
            e.HasIndex(t => t.RecurringOccurrenceId).IsUnique().HasFilter("\"RecurringOccurrenceId\" IS NOT NULL");
            e.HasOne(t => t.Account).WithMany(a => a.Transactions).HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.TransferAccount).WithMany().HasForeignKey(t => t.TransferAccountId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(t => t.Category).WithMany(c => c.Transactions).HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(t => t.RefundOf).WithMany().HasForeignKey(t => t.RefundOfId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Budget>(e =>
        {
            e.Property(b => b.MonthlyLimit).HasColumnType(money);
            e.HasOne(b => b.Category).WithMany(c => c.Budgets).HasForeignKey(b => b.CategoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(b => new { b.CategoryId, b.Month, b.Year }).IsUnique();
        });

        modelBuilder.Entity<BudgetTemplate>(e =>
        {
            e.Property(b => b.MonthlyLimit).HasColumnType(money);
            e.HasOne(b => b.Category).WithMany().HasForeignKey(b => b.CategoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(b => new { b.CategoryId, b.EffectiveYear, b.EffectiveMonth }).IsUnique();
        });

        modelBuilder.Entity<BudgetAlert>(e =>
        {
            e.HasOne(a => a.Budget).WithMany().HasForeignKey(a => a.BudgetId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Category).WithMany().HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecurringItem>(e =>
        {
            e.ToTable("RecurringItems");
            e.Property(r => r.Amount).HasColumnType(money);
            e.Property(r => r.Version).IsRowVersion();
            e.HasIndex(r => r.SeriesId);
            e.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Account).WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.Category).WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RecurringOccurrence>(e =>
        {
            e.Property(o => o.Amount).HasColumnType(money);
            e.Property(o => o.Version).IsRowVersion();
            // One occurrence per series per month: generation can run any number of times without duplicates.
            e.HasIndex(o => new { o.SeriesId, o.Year, o.Month }).IsUnique();
            e.HasIndex(o => new { o.UserId, o.Year, o.Month });
            e.HasOne(o => o.RecurringItem).WithMany().HasForeignKey(o => o.RecurringItemId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SalaryPlan>(e =>
        {
            e.ToTable("SalaryRecords");
            e.Property(s => s.Amount).HasColumnType(money);
            e.Property(s => s.Allowances).HasColumnType(money);
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.UserId, s.Year, s.Month }).IsUnique();
        });

        modelBuilder.Entity<SalaryStatement>(e =>
        {
            e.Property(s => s.Version).IsRowVersion();
            e.HasIndex(s => new { s.UserId, s.EarnedYear, s.EarnedMonth }).IsUnique();
            e.HasIndex(s => new { s.UserId, s.FundedYear, s.FundedMonth });
            e.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SalaryComponent>(e =>
        {
            e.Property(c => c.Amount).HasColumnType(money);
            e.HasOne(c => c.Statement).WithMany(s => s.Components).HasForeignKey(c => c.StatementId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IncomeReceipt>(e =>
        {
            e.Property(r => r.Amount).HasColumnType(money);
            e.Property(r => r.Version).IsRowVersion();
            e.HasIndex(r => new { r.UserId, r.ClientRequestId }).IsUnique().HasFilter("\"ClientRequestId\" IS NOT NULL");
            e.HasIndex(r => r.RecurringOccurrenceId).IsUnique().HasFilter("\"RecurringOccurrenceId\" IS NOT NULL");
            e.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.SalaryStatement).WithMany().HasForeignKey(r => r.SalaryStatementId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Category).WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<IncomeAllocation>(e =>
        {
            e.Property(a => a.Amount).HasColumnType(money);
            e.HasOne(a => a.Receipt).WithMany(r => r.Allocations).HasForeignKey(a => a.ReceiptId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.Year, a.Month });
        });

        modelBuilder.Entity<MonthSetting>(e =>
        {
            e.Property(m => m.OpeningBalance).HasColumnType(money);
            e.Property(m => m.ReservedSavings).HasColumnType(money);
            e.Property(m => m.Version).IsRowVersion();
            e.HasIndex(m => new { m.UserId, m.Year, m.Month }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomColumn>(e =>
        {
            e.Property(c => c.OptionsJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(c => c.Version).IsRowVersion();
            e.HasIndex(c => new { c.UserId, c.TableKey, c.Key }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TableLayout>(e =>
        {
            e.Property(l => l.ColumnsJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.HasIndex(l => new { l.UserId, l.TableKey, l.Scope, l.Year, l.Month }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MonthAdjustment>(e =>
        {
            e.Property(m => m.SalaryAdjust).HasColumnType(money);
            e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => new { m.UserId, m.Year, m.Month }).IsUnique();
        });

        modelBuilder.Entity<DayNote>(e =>
        {
            e.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => new { d.UserId, d.Date }).IsUnique();
        });
    }
}
