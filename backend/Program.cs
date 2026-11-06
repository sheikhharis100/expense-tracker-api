using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using System.Text;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Hubs;
using ExpenseTracker.Api.Ledger;
using ExpenseTracker.Api.Services;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "ExpenseTracker API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(NormalizeConnectionString(builder.Configuration.GetConnectionString("DefaultConnection")!)));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    };

    // Browsers can't set headers on WebSocket requests, so SignalR sends the token in the query string.
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var token = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                context.Token = token;
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

builder.Services.Configure<CurrencyRatesOptions>(builder.Configuration.GetSection("CurrencyRates"));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<CurrencyConverter>();
builder.Services.AddScoped<LedgerLoader>();
builder.Services.AddScoped<ColumnService>();
builder.Services.AddScoped<ExpenseService>();
builder.Services.AddScoped<IncomeService>();
builder.Services.AddScoped<RecurringService>();
builder.Services.AddScoped<BudgetService>();
builder.Services.AddScoped<MonthService>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<CsvImportService>();
builder.Services.AddScoped<SheetImportService>();
builder.Services.AddScoped<LegacyBackfillService>();
builder.Services.AddSingleton<LedgerNotifier>();
if (builder.Configuration.GetValue("BackgroundJobs:Enabled", true))
    builder.Services.AddHostedService<BudgetCheckBackgroundService>();

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    // One-time, per-user upgrade of records from the first version (see LegacyBackfillService).
    await scope.ServiceProvider.GetRequiredService<LegacyBackfillService>().RunAsync();
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        context.Response.StatusCode = feature?.Error is UnauthorizedAccessException
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        var message = app.Environment.IsDevelopment() && feature?.Error != null
            ? feature.Error.Message
            : "An unexpected error occurred.";
        await context.Response.WriteAsJsonAsync(new { error = message });
    });
});

// Liveness only: never touches the database, so uptime pings don't wake (or use up) the free Postgres.
app.MapGet("/healthz", () => Results.Text("ok"));

app.UseSwagger();
app.UseSwaggerUI();

// The React app is built into wwwroot for deployment; in development Vite serves it and proxies /api here.
var hasFrontend = File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html"));
if (hasFrontend)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
else
{
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<LedgerHub>("/hubs/ledger");

if (hasFrontend)
{
    // Client-side routes (/log, /months, …) load the app; unknown /api paths still 404.
    app.MapFallbackToFile("{*path:regex(^(?!api/|hubs/|swagger).*$)}", "index.html");
}

app.Run();

static string NormalizeConnectionString(string raw)
{
    if (!raw.StartsWith("postgres://") && !raw.StartsWith("postgresql://"))
        return raw;

    var uri = new Uri(raw);
    var userInfo = uri.UserInfo.Split(':', 2);

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Username = Uri.UnescapeDataString(userInfo[0]),
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
        Database = uri.AbsolutePath.TrimStart('/'),
        SslMode = SslMode.Require
    };

    return builder.ToString();
}

// Lets integration tests host the app with WebApplicationFactory.
public partial class Program;
