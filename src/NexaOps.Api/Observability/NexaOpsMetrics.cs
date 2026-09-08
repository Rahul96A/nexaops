using System.Diagnostics.Metrics;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Api;

/// <summary>
/// Product metrics, distinct from infrastructure metrics.
/// <para>
/// These answer questions a service desk manager asks - how much work is arriving, how often are
/// we breaching, how much is the AI being used - rather than questions about CPU and memory.
/// </para>
/// </summary>
public sealed class NexaOpsMetrics
{
    public const string MeterName = "NexaOps";

    private readonly Counter<long> _incidentsCreated;
    private readonly Counter<long> _incidentsResolved;
    private readonly Counter<long> _slaBreached;
    private readonly Counter<long> _slaWarnings;
    private readonly Counter<long> _aiToolInvocations;
    private readonly Counter<long> _authenticationFailures;
    private readonly Histogram<double> _incidentResolutionMinutes;

    public NexaOpsMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MeterName);

        _incidentsCreated = meter.CreateCounter<long>(
            "nexaops.incidents.created", "incidents", "Incidents raised.");

        _incidentsResolved = meter.CreateCounter<long>(
            "nexaops.incidents.resolved", "incidents", "Incidents resolved.");

        _slaBreached = meter.CreateCounter<long>(
            "nexaops.sla.breached", "clocks", "SLA commitments breached.");

        _slaWarnings = meter.CreateCounter<long>(
            "nexaops.sla.warned", "clocks", "SLA warning thresholds crossed.");

        _aiToolInvocations = meter.CreateCounter<long>(
            "nexaops.ai.tool.invoked", "calls", "AI tool invocations.");

        _authenticationFailures = meter.CreateCounter<long>(
            "nexaops.auth.failed", "attempts", "Failed authentication attempts.");

        _incidentResolutionMinutes = meter.CreateHistogram<double>(
            "nexaops.incidents.resolution_minutes", "minutes", "Wall-clock time from creation to resolution.");
    }

    public void IncidentCreated(Priority priority)
        => _incidentsCreated.Add(1, new KeyValuePair<string, object?>("priority", priority.ToString()));

    public void IncidentResolved(Priority priority, double elapsedMinutes)
    {
        var tag = new KeyValuePair<string, object?>("priority", priority.ToString());
        _incidentsResolved.Add(1, tag);
        _incidentResolutionMinutes.Record(elapsedMinutes, tag);
    }

    public void SlaBreached(string targetType)
        => _slaBreached.Add(1, new KeyValuePair<string, object?>("target", targetType));

    public void SlaWarned(string targetType)
        => _slaWarnings.Add(1, new KeyValuePair<string, object?>("target", targetType));

    public void AiToolInvoked(string toolName, bool succeeded)
        => _aiToolInvocations.Add(
            1,
            new KeyValuePair<string, object?>("tool", toolName),
            new KeyValuePair<string, object?>("succeeded", succeeded));

    public void AuthenticationFailed(string reason)
        => _authenticationFailures.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
