using System.Net.Http.Json;
using System.Threading.Channels;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ExpenseTracker.Api.Services;

public record EmailMessage(string To, string Subject, string Html, string Text);

public interface IEmailSender
{
    // False when messages would go nowhere a person can read them; the app then hides email features.
    bool CanDeliver { get; }
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

// The "Email" configuration section.
public class EmailOptions
{
    // Brevo (free plan: 300 emails a day) sends over HTTPS, which matters on Render: free instances block outbound SMTP.
    public string? BrevoApiKey { get; set; }
    // Plain SMTP, e.g. Mailpit catching mail in local Docker. Used when no Brevo key is set.
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 1025;
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpTls { get; set; }
    // For Brevo this must be a sender verified there.
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Expense Tracker";
}

public static class EmailSetup
{
    // Brevo when Email:BrevoApiKey is set, else SMTP when Email:SmtpHost is set, else the log (no email).
    public static IServiceCollection AddEmailSender(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection("Email");
        services.Configure<EmailOptions>(section);
        var options = section.Get<EmailOptions>() ?? new EmailOptions();

        if (!string.IsNullOrWhiteSpace(options.BrevoApiKey))
            services.AddHttpClient<IEmailSender, BrevoEmailSender>(c =>
            {
                c.BaseAddress = new Uri(BrevoEmailSender.BaseAddress);
                c.Timeout = TimeSpan.FromSeconds(15);
                c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            });
        else if (!string.IsNullOrWhiteSpace(options.SmtpHost))
            services.AddTransient<IEmailSender, SmtpEmailSender>();
        else
            services.AddTransient<IEmailSender, LogEmailSender>();

        services.AddSingleton<EmailOutbox>();
        services.AddHostedService(sp => sp.GetRequiredService<EmailOutbox>());
        return services;
    }
}

// Brevo transactional email API: POST https://api.brevo.com/v3/smtp/email.
public sealed class BrevoEmailSender : IEmailSender
{
    public const string BaseAddress = "https://api.brevo.com/";

    private readonly HttpClient _http;
    private readonly EmailOptions _options;

    public BrevoEmailSender(HttpClient http, IOptions<EmailOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    // Brevo refuses mail without a verified sender.
    public bool CanDeliver => !string.IsNullOrWhiteSpace(_options.FromAddress);

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/smtp/email")
        {
            Content = JsonContent.Create(new
            {
                sender = new { name = _options.FromName, email = _options.FromAddress },
                to = new[] { new { email = message.To } },
                subject = message.Subject,
                htmlContent = message.Html,
                textContent = message.Text
            })
        };
        request.Headers.Add("api-key", _options.BrevoApiKey);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Brevo answered {(int)response.StatusCode}: {detail[..Math.Min(detail.Length, 300)]}");
        }
    }
}

// Any SMTP server; locally that's Mailpit (Email:SmtpHost=mailpit in docker compose), which shows mail at :8025.
public sealed class SmtpEmailSender : IEmailSender
{
    public const string DefaultFromAddress = "no-reply@expense-tracker.local";

    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options) => _options = options.Value;

    public bool CanDeliver => true;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var from = string.IsNullOrWhiteSpace(_options.FromAddress) ? DefaultFromAddress : _options.FromAddress;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, from));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();

        using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = 15_000 };
        await client.ConnectAsync(_options.SmtpHost!, _options.SmtpPort,
            _options.SmtpTls ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.None, ct);
        if (!string.IsNullOrEmpty(_options.SmtpUser))
            await client.AuthenticateAsync(_options.SmtpUser, _options.SmtpPassword ?? string.Empty, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
    }
}

// Used when no email service is configured. In Development the message (link included) goes to the console so the
// flows can be tried locally; anywhere else only a warning is logged, never the link, since logs aren't private.
public sealed class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _logger;
    private readonly IHostEnvironment _env;

    public LogEmailSender(ILogger<LogEmailSender> logger, IHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public bool CanDeliver => _env.IsDevelopment();

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (_env.IsDevelopment())
            _logger.LogInformation("Email to {To}: {Subject}\n{Text}", message.To, message.Subject, message.Text);
        else
            _logger.LogWarning("Email \"{Subject}\" not sent: set Email:BrevoApiKey (or Email:SmtpHost) to send email", message.Subject);
        return Task.CompletedTask;
    }
}

// Requests queue their emails here and return at once: a slow email service never holds up a request, and an
// address with an account answers as fast as one without (no account discovery by timing).
public sealed class EmailOutbox : BackgroundService
{
    private readonly Channel<EmailMessage> _queue = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EmailOutbox> _logger;

    public EmailOutbox(IServiceScopeFactory scopes, ILogger<EmailOutbox> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public void Queue(EmailMessage message)
    {
        if (!_queue.Writer.TryWrite(message))
            _logger.LogWarning("Email queue full; \"{Subject}\" dropped", message.Subject);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // The person can ask again; one failed email mustn't stop the rest.
                _logger.LogError(ex, "Could not send email \"{Subject}\"", message.Subject);
            }
        }
    }
}
