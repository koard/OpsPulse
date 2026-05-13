namespace Platform.Domain;

public static class IncidentPolicy
{
    public static IReadOnlyList<Incident> ApplyAlerts(
        IReadOnlyList<Incident> existingIncidents,
        ProjectSnapshot snapshot,
        IEnumerable<AlertEvent> alerts)
    {
        ArgumentNullException.ThrowIfNull(existingIncidents);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(alerts);

        var incidents = existingIncidents.ToDictionary(
            incident => incident.Fingerprint,
            StringComparer.OrdinalIgnoreCase);
        var rootCauseHint = RootCauseAnalyzer.Analyze(snapshot).FirstOrDefault()?.Title
            ?? "Needs investigation";

        foreach (var alert in alerts)
        {
            var fingerprint = $"{snapshot.ProjectId}:{alert.Id}".ToLowerInvariant();
            if (incidents.TryGetValue(fingerprint, out var incident))
            {
                var isReopened = incident.Status == IncidentStatus.Resolved;
                incidents[fingerprint] = incident with
                {
                    Severity = MaxSeverity(incident.Severity, alert.Severity),
                    Status = isReopened ? IncidentStatus.Open : incident.Status,
                    LastSeenAt = snapshot.CapturedAt,
                    RootCauseHint = rootCauseHint,
                    Occurrences = incident.Occurrences + 1,
                    AcknowledgedAt = isReopened ? null : incident.AcknowledgedAt,
                    ResolvedAt = null
                };
                continue;
            }

            incidents[fingerprint] = new Incident(
                Id: StableIncidentId(fingerprint),
                ProjectId: snapshot.ProjectId,
                Fingerprint: fingerprint,
                Severity: alert.Severity,
                Title: alert.Title,
                Summary: alert.Message,
                Status: IncidentStatus.Open,
                StartedAt: alert.CreatedAt,
                LastSeenAt: snapshot.CapturedAt,
                RootCauseHint: rootCauseHint,
                Occurrences: 1);
        }

        return incidents.Values
            .OrderByDescending(incident => incident.Status == IncidentStatus.Open)
            .ThenByDescending(incident => incident.LastSeenAt)
            .ToList();
    }

    private static AlertSeverity MaxSeverity(AlertSeverity left, AlertSeverity right)
    {
        return (AlertSeverity)Math.Max((int)left, (int)right);
    }

    private static string StableIncidentId(string fingerprint)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(fingerprint)))[..16].ToLowerInvariant();
    }
}
