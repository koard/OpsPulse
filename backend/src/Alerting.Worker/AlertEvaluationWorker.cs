using System.Net.Http.Json;
using Platform.Domain;

namespace Alerting.Worker;

public sealed class AlertEvaluationWorker(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    EmailNotifier emailNotifier,
    ILogger<AlertEvaluationWorker> logger) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(
        configuration.GetValue("Alerting:IntervalSeconds", 30));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await EvaluateOnce(stoppingToken);
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task EvaluateOnce(CancellationToken stoppingToken)
    {
        var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";
        var projectId = configuration["Alerting:ProjectId"] ?? "dukefarm";
        var http = httpClientFactory.CreateClient();

        try
        {
            var snapshot = await http.GetFromJsonAsync<ProjectSnapshot>(
                $"{telemetryUrl}/snapshots/latest/{projectId}",
                stoppingToken);

            if (snapshot is null)
            {
                logger.LogWarning("No telemetry snapshot found for project {ProjectId}", projectId);
                return;
            }

            var alerts = AlertEvaluator.Evaluate(snapshot).ToList();

            foreach (var alert in alerts)
            {
                logger.LogWarning(
                    "Alert {Severity}: {Title} - {Message}",
                    alert.Severity,
                    alert.Title,
                    alert.Message);
            }

            if (alerts.Count > 0)
            {
                await emailNotifier.NotifyAsync(alerts, stoppingToken);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Alert evaluation skipped because telemetry is unavailable.");
        }
    }
}
