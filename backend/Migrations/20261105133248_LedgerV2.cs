using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExpenseTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class LedgerV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CarryoverEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DataVersion",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "Asia/Karachi");

            migrationBuilder.AlterColumn<int>(
                name: "CategoryId",
                table: "Transactions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomValuesJson",
                table: "Transactions",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<bool>(
                name: "IsAdditional",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Expense");

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Transactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RecurringOccurrenceId",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefundOfId",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Paid");

            migrationBuilder.AddColumn<int>(
                name: "TransferAccountId",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Transactions",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Each transaction belongs to its account's user.
            migrationBuilder.Sql(@"UPDATE ""Transactions"" t SET ""UserId"" = a.""UserId"", ""UpdatedAt"" = t.""CreatedAt""
                FROM ""Accounts"" a WHERE a.""Id"" = t.""AccountId"";");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Transactions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<decimal>(
                name: "Allowances",
                table: "SalaryRecords",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<int>(
                name: "BudgetId",
                table: "BudgetAlerts",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "BudgetAlerts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Month",
                table: "BudgetAlerts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Year",
                table: "BudgetAlerts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BudgetTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    EffectiveYear = table.Column<int>(type: "integer", nullable: false),
                    EffectiveMonth = table.Column<int>(type: "integer", nullable: false),
                    MonthlyLimit = table.Column<decimal>(type: "numeric(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetTemplates_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomColumns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TableKey = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DataType = table.Column<string>(type: "text", nullable: false),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    Formula = table.Column<string>(type: "text", nullable: false),
                    Decimals = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomColumns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomColumns_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonthSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ReservedSavings = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<int>(type: "integer", nullable: true),
                    CategoryId = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: false),
                    PaymentMethod = table.Column<string>(type: "text", nullable: false),
                    StartYear = table.Column<int>(type: "integer", nullable: false),
                    StartMonth = table.Column<int>(type: "integer", nullable: false),
                    EndYear = table.Column<int>(type: "integer", nullable: true),
                    EndMonth = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringItems_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecurringItems_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecurringItems_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalaryStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    EarnedYear = table.Column<int>(type: "integer", nullable: false),
                    EarnedMonth = table.Column<int>(type: "integer", nullable: false),
                    FundedYear = table.Column<int>(type: "integer", nullable: false),
                    FundedMonth = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryStatements_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TableLayouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TableKey = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    ColumnsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableLayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TableLayouts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringOccurrences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RecurringItemId = table.Column<int>(type: "integer", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    IsOverridden = table.Column<bool>(type: "boolean", nullable: false),
                    TransactionId = table.Column<int>(type: "integer", nullable: true),
                    IncomeReceiptId = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringOccurrences_RecurringItems_RecurringItemId",
                        column: x => x.RecurringItemId,
                        principalTable: "RecurringItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncomeReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    SalaryStatementId = table.Column<int>(type: "integer", nullable: true),
                    CategoryId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ReceivedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpectedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    PaymentMethod = table.Column<string>(type: "text", nullable: false),
                    RecurringOccurrenceId = table.Column<int>(type: "integer", nullable: true),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomeReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncomeReceipts_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IncomeReceipts_SalaryStatements_SalaryStatementId",
                        column: x => x.SalaryStatementId,
                        principalTable: "SalaryStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncomeReceipts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalaryComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StatementId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryComponents_SalaryStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "SalaryStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncomeAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReceiptId = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomeAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncomeAllocations_IncomeReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "IncomeReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Category budgets applied to every month; keep that as a template effective from the start.
            migrationBuilder.Sql(@"INSERT INTO ""BudgetTemplates"" (""CategoryId"", ""EffectiveYear"", ""EffectiveMonth"", ""MonthlyLimit"")
                SELECT ""Id"", 2000, 1, ""MonthlyBudget"" FROM ""Categories"" WHERE ""MonthlyBudget"" IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "MonthlyBudget",
                table: "Categories");

            // Old recurring transactions become recurring series starting at their next due month, so months they
            // already posted for are not planned again.
            migrationBuilder.Sql(@"INSERT INTO ""RecurringItems"" (""UserId"", ""SeriesId"", ""Kind"", ""Description"", ""AccountId"", ""CategoryId"",
                    ""Amount"", ""DayOfMonth"", ""PaymentMethod"", ""StartYear"", ""StartMonth"", ""EndYear"", ""EndMonth"", ""IsActive"", ""CreatedAt"")
                SELECT ""UserId"", gen_random_uuid(), 'Expense', ""Description"", ""AccountId"", ""CategoryId"",
                    ""Amount"", ""DayOfMonth"", '', EXTRACT(YEAR FROM ""NextRunDate"")::int, EXTRACT(MONTH FROM ""NextRunDate"")::int,
                    NULL, NULL, ""IsActive"", ""CreatedAt""
                FROM ""RecurringTransactions"";");

            migrationBuilder.DropTable(
                name: "RecurringTransactions");

            // Alerts now point at a category and month.
            migrationBuilder.Sql(@"UPDATE ""BudgetAlerts"" ba SET ""CategoryId"" = b.""CategoryId"", ""Year"" = b.""Year"", ""Month"" = b.""Month""
                FROM ""Budgets"" b WHERE b.""Id"" = ba.""BudgetId"";");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_RecurringOccurrenceId",
                table: "Transactions",
                column: "RecurringOccurrenceId",
                unique: true,
                filter: "\"RecurringOccurrenceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_RefundOfId",
                table: "Transactions",
                column: "RefundOfId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransferAccountId",
                table: "Transactions",
                column: "TransferAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_ClientRequestId",
                table: "Transactions",
                columns: new[] { "UserId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_TransactionDate",
                table: "Transactions",
                columns: new[] { "UserId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAlerts_CategoryId",
                table: "BudgetAlerts",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTemplates_CategoryId_EffectiveYear_EffectiveMonth",
                table: "BudgetTemplates",
                columns: new[] { "CategoryId", "EffectiveYear", "EffectiveMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomColumns_UserId_TableKey_Key",
                table: "CustomColumns",
                columns: new[] { "UserId", "TableKey", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomeAllocations_ReceiptId",
                table: "IncomeAllocations",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_IncomeAllocations_Year_Month",
                table: "IncomeAllocations",
                columns: new[] { "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_IncomeReceipts_CategoryId",
                table: "IncomeReceipts",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_IncomeReceipts_RecurringOccurrenceId",
                table: "IncomeReceipts",
                column: "RecurringOccurrenceId",
                unique: true,
                filter: "\"RecurringOccurrenceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IncomeReceipts_SalaryStatementId",
                table: "IncomeReceipts",
                column: "SalaryStatementId");

            migrationBuilder.CreateIndex(
                name: "IX_IncomeReceipts_UserId_ClientRequestId",
                table: "IncomeReceipts",
                columns: new[] { "UserId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MonthSettings_UserId_Year_Month",
                table: "MonthSettings",
                columns: new[] { "UserId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringItems_AccountId",
                table: "RecurringItems",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringItems_CategoryId",
                table: "RecurringItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringItems_SeriesId",
                table: "RecurringItems",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringItems_UserId",
                table: "RecurringItems",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrences_RecurringItemId",
                table: "RecurringOccurrences",
                column: "RecurringItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrences_SeriesId_Year_Month",
                table: "RecurringOccurrences",
                columns: new[] { "SeriesId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrences_UserId_Year_Month",
                table: "RecurringOccurrences",
                columns: new[] { "UserId", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryComponents_StatementId",
                table: "SalaryComponents",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStatements_UserId_EarnedYear_EarnedMonth",
                table: "SalaryStatements",
                columns: new[] { "UserId", "EarnedYear", "EarnedMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalaryStatements_UserId_FundedYear_FundedMonth",
                table: "SalaryStatements",
                columns: new[] { "UserId", "FundedYear", "FundedMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_TableLayouts_UserId_TableKey_Scope_Year_Month",
                table: "TableLayouts",
                columns: new[] { "UserId", "TableKey", "Scope", "Year", "Month" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BudgetAlerts_Categories_CategoryId",
                table: "BudgetAlerts",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Accounts_TransferAccountId",
                table: "Transactions",
                column: "TransferAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Transactions_RefundOfId",
                table: "Transactions",
                column: "RefundOfId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BudgetAlerts_Categories_CategoryId",
                table: "BudgetAlerts");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Accounts_TransferAccountId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Transactions_RefundOfId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "BudgetTemplates");

            migrationBuilder.DropTable(
                name: "CustomColumns");

            migrationBuilder.DropTable(
                name: "IncomeAllocations");

            migrationBuilder.DropTable(
                name: "MonthSettings");

            migrationBuilder.DropTable(
                name: "RecurringOccurrences");

            migrationBuilder.DropTable(
                name: "SalaryComponents");

            migrationBuilder.DropTable(
                name: "TableLayouts");

            migrationBuilder.DropTable(
                name: "IncomeReceipts");

            migrationBuilder.DropTable(
                name: "RecurringItems");

            migrationBuilder.DropTable(
                name: "SalaryStatements");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_RecurringOccurrenceId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_RefundOfId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_TransferAccountId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId_ClientRequestId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId_TransactionDate",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_BudgetAlerts_CategoryId",
                table: "BudgetAlerts");

            migrationBuilder.DropColumn(
                name: "CarryoverEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DataVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CustomValuesJson",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IsAdditional",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RecurringOccurrenceId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RefundOfId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TransferAccountId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Allowances",
                table: "SalaryRecords");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "BudgetAlerts");

            migrationBuilder.DropColumn(
                name: "Month",
                table: "BudgetAlerts");

            migrationBuilder.DropColumn(
                name: "Year",
                table: "BudgetAlerts");

            migrationBuilder.AlterColumn<int>(
                name: "CategoryId",
                table: "Transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyBudget",
                table: "Categories",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "BudgetId",
                table: "BudgetAlerts",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    NextRunDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringTransactions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecurringTransactions_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecurringTransactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactions_AccountId",
                table: "RecurringTransactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactions_CategoryId",
                table: "RecurringTransactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactions_UserId",
                table: "RecurringTransactions",
                column: "UserId");
        }
    }
}
