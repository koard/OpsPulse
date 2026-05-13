using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Platform.Domain;

namespace Alerting.Worker;

/// <summary>
/// Sends email notifications for alerts.
/// Includes per-alert cooldown to prevent spamming the same alert repeatedly.
/// </summary>
public sealed class EmailNotifier(IConfiguration configuration, ILogger<EmailNotifier> logger)
{
    // Track when each alert ID was last emailed to enforce cooldown
    private readonly Dictionary<string, DateTimeOffset> _lastSent = [];

    private TimeSpan CooldownDuration => TimeSpan.FromMinutes(
        configuration.GetValue("Alerting:Email:CooldownMinutes", 30));

    public async Task NotifyAsync(IReadOnlyList<AlertEvent> alerts, CancellationToken cancellationToken)
    {
        if (!IsEmailConfigured())
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var toSend = alerts
            .Where(alert => !_lastSent.TryGetValue(alert.Id, out var last) || now - last >= CooldownDuration)
            .ToList();

        if (toSend.Count == 0)
        {
            return;
        }

        try
        {
            await SendEmailAsync(toSend, cancellationToken);

            foreach (var alert in toSend)
            {
                _lastSent[alert.Id] = now;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send alert email notification.");
        }
    }

    private async Task SendEmailAsync(List<AlertEvent> alerts, CancellationToken cancellationToken)
    {
        var host     = configuration["Alerting:Email:SmtpHost"]!;
        var port     = configuration.GetValue("Alerting:Email:SmtpPort", 587);
        var username = configuration["Alerting:Email:Username"]!;
        var password = configuration["Alerting:Email:Password"]!;
        var from     = configuration["Alerting:Email:From"] ?? username;
        var to       = configuration["Alerting:Email:To"]!;

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = BuildSubject(alerts);

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = BuildHtmlBody(alerts),
            TextBody = BuildTextBody(alerts),
        };
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(host, port, SecureSocketOptions.StartTls, cancellationToken);
        await client.AuthenticateAsync(username, password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        logger.LogInformation("Alert email sent: {Subject}", message.Subject);
    }

    private static string BuildSubject(List<AlertEvent> alerts)
    {
        var critical = alerts.Count(a => a.Severity == AlertSeverity.Critical);
        var warning  = alerts.Count(a => a.Severity == AlertSeverity.Warning);

        if (critical > 0)
            return $"🔴 OpsPulse: {critical} critical alert{(critical > 1 ? "s" : "")} detected";

        return $"🟡 OpsPulse: {warning} warning{(warning > 1 ? "s" : "")} detected";
    }

    private static string BuildHtmlBody(List<AlertEvent> alerts)
    {
        var rows = string.Join("\n", alerts.Select(a =>
        {
            var color = a.Severity == AlertSeverity.Critical ? "#ef4444" : "#f59e0b";
            var icon  = a.Severity == AlertSeverity.Critical ? "🔴" : "🟡";
            return $"""
                <tr>
                  <td style="padding:12px 16px;border-bottom:1px solid #1e293b;">
                    <span style="font-weight:700;color:{color}">{icon} {a.Title}</span><br/>
                    <span style="color:#94a3b8;font-size:13px">{a.Message}</span>
                  </td>
                </tr>
            """;
        }));

        return $"""
            <!DOCTYPE html>
            <html>
            <body style="margin:0;padding:0;background:#0f172a;font-family:system-ui,sans-serif;color:#e2e8f0;">
              <div style="max-width:580px;margin:40px auto;background:#1e293b;border-radius:12px;overflow:hidden;border:1px solid #334155;">
                <div style="padding:24px 28px;border-bottom:1px solid #334155;">
                  <h1 style="margin:0;font-size:20px;font-weight:700;color:#f1f5f9">
                    ⚡ OpsPulse Alert
                  </h1>
                  <p style="margin:6px 0 0;color:#94a3b8;font-size:14px">
                    {alerts.Count} active alert{(alerts.Count > 1 ? "s" : "")} require your attention
                  </p>
                </div>
                <table style="width:100%;border-collapse:collapse;">
                  {rows}
                </table>
                <div style="padding:16px 28px;color:#475569;font-size:12px;">
                  Generated at {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC · OpsPulse Monitoring
                </div>
              </div>
            </body>
            </html>
        """;
    }

    private static string BuildTextBody(List<AlertEvent> alerts)
    {
        var lines = alerts.Select(a =>
            $"[{a.Severity.ToString().ToUpper()}] {a.Title}\n  {a.Message}");
        return $"OpsPulse Alert — {alerts.Count} active alert(s)\n\n{string.Join("\n\n", lines)}";
    }

    private bool IsEmailConfigured() =>
        !string.IsNullOrWhiteSpace(configuration["Alerting:Email:SmtpHost"]) &&
        !string.IsNullOrWhiteSpace(configuration["Alerting:Email:Username"]) &&
        !string.IsNullOrWhiteSpace(configuration["Alerting:Email:Password"]) &&
        !string.IsNullOrWhiteSpace(configuration["Alerting:Email:To"]);
}
