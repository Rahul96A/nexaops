using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Security;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;

namespace NexaOps.Application.Reporting;

/// <summary>Read models for reporting. No tracking, aggregated in SQL.</summary>
public interface IReportQueryService
{
    Task<ServiceDeskReportDto> GetServiceDeskAsync(ReportPeriod period, CancellationToken ct = default);

    Task<SlaReportDto> GetSlaAttainmentAsync(ReportPeriod period, CancellationToken ct = default);

    Task<ChangeReportDto> GetChangeDeliveryAsync(ReportPeriod period, CancellationToken ct = default);

    Task<RequestReportDto> GetRequestDeliveryAsync(ReportPeriod period, CancellationToken ct = default);
}

/// <summary>
/// Operational reporting over a period.
/// <para>
/// Every figure here is computed from records the tenant actually holds. Where a figure cannot
/// be computed — no resolutions in the window, a rate with no denominator — the answer is
/// absent rather than zero, because zero is a measurement and "we cannot say" is not.
/// </para>
/// </summary>
public interface IReportService
{
    Task<ServiceDeskReportDto> GetServiceDeskAsync(ReportPeriod period, CancellationToken ct = default);

    Task<SlaReportDto> GetSlaAttainmentAsync(ReportPeriod period, CancellationToken ct = default);

    Task<ChangeReportDto> GetChangeDeliveryAsync(ReportPeriod period, CancellationToken ct = default);

    Task<RequestReportDto> GetRequestDeliveryAsync(ReportPeriod period, CancellationToken ct = default);

    /// <summary>
    /// Renders one report as CSV.
    /// <para>
    /// Held behind a separate permission from viewing it: a figure on a screen stays inside the
    /// application, and a file leaves with whoever downloaded it.
    /// </para>
    /// </summary>
    Task<ReportExport> ExportAsync(string report, ReportPeriod period, CancellationToken ct = default);
}

/// <param name="FileName">Suggested download name, dated so two exports do not collide.</param>
/// <param name="Content">The CSV itself.</param>
public sealed record ReportExport(string FileName, string Content);

/// <inheritdoc />
public sealed class ReportService : IReportService
{
    /// <summary>
    /// The longest window a report may cover.
    /// <para>
    /// Not a licensing limit: the daily series is materialised per day and the breakdowns scan
    /// the period, so an unbounded range is a way for one request to occupy the database for
    /// minutes. A year is longer than any question anybody asks of an operational report.
    /// </para>
    /// </summary>
    private const int MaxPeriodDays = 366;

    private readonly IReportQueryService _queries;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        IReportQueryService queries,
        IAuditService audit,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<ReportService> logger)
    {
        _queries = queries;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ServiceDeskReportDto> GetServiceDeskAsync(ReportPeriod period, CancellationToken ct = default)
        => _queries.GetServiceDeskAsync(Validate(period), ct);

    /// <inheritdoc />
    public Task<SlaReportDto> GetSlaAttainmentAsync(ReportPeriod period, CancellationToken ct = default)
        => _queries.GetSlaAttainmentAsync(Validate(period), ct);

    /// <inheritdoc />
    public Task<ChangeReportDto> GetChangeDeliveryAsync(ReportPeriod period, CancellationToken ct = default)
        => _queries.GetChangeDeliveryAsync(Validate(period), ct);

    /// <inheritdoc />
    public Task<RequestReportDto> GetRequestDeliveryAsync(ReportPeriod period, CancellationToken ct = default)
        => _queries.GetRequestDeliveryAsync(Validate(period), ct);

    /// <inheritdoc />
    public async Task<ReportExport> ExportAsync(
        string report,
        ReportPeriod period,
        CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.ReportExport);

        var window = Validate(period);

        var (name, csv) = report?.ToLowerInvariant() switch
        {
            "service-desk" => ("service-desk", await ServiceDeskCsvAsync(window, ct).ConfigureAwait(false)),
            "sla" => ("sla-attainment", await SlaCsvAsync(window, ct).ConfigureAwait(false)),
            "changes" => ("change-delivery", await ChangeCsvAsync(window, ct).ConfigureAwait(false)),
            "requests" => ("request-delivery", await RequestCsvAsync(window, ct).ConfigureAwait(false)),

            _ => throw new DomainException(
                "report.unknown",
                $"There is no report called \"{report}\".")
        };

        // Exports are audited because they are the point at which data leaves the application.
        // Viewing a figure is not; downloading the rows behind it is.
        _audit.Record(
            AuditAction.Export,
            "Report",
            name,
            name,
            $"Exported the {name} report for {window.From:yyyy-MM-dd} to {window.To:yyyy-MM-dd}.");

        _logger.LogInformation(
            "Report {Report} exported for {From} to {To}.", name, window.From, window.To);

        return new ReportExport($"nexaops-{name}-{window.From:yyyyMMdd}-{window.To:yyyyMMdd}.csv", csv);
    }

    private async Task<string> ServiceDeskCsvAsync(ReportPeriod period, CancellationToken ct)
    {
        var report = await _queries.GetServiceDeskAsync(period, ct).ConfigureAwait(false);

        return CsvWriter.Write(
            ["Date", "Created", "Resolved"],
            report.Daily.Select(d => new object?[] { d.Date, d.Created, d.Resolved }));
    }

    private async Task<string> SlaCsvAsync(ReportPeriod period, CancellationToken ct)
    {
        var report = await _queries.GetSlaAttainmentAsync(period, ct).ConfigureAwait(false);

        return CsvWriter.Write(
            ["Target", "Priority", "Met", "Breached", "Attainment %"],
            report.Rows.Select(r => new object?[]
            {
                r.Target.ToString(),
                r.Priority?.ToString() ?? "All",
                r.Met,
                r.Breached,
                r.Attainment.Percent
            }));
    }

    private async Task<string> ChangeCsvAsync(ReportPeriod period, CancellationToken ct)
    {
        var report = await _queries.GetChangeDeliveryAsync(period, ct).ConfigureAwait(false);

        return CsvWriter.Write(
            ["Outcome", "Count"],
            report.Outcomes.Select(o => new object?[] { o.Outcome.ToString(), o.Count }));
    }

    private async Task<string> RequestCsvAsync(ReportPeriod period, CancellationToken ct)
    {
        var report = await _queries.GetRequestDeliveryAsync(period, ct).ConfigureAwait(false);

        return CsvWriter.Write(
            ["Catalogue item", "Raised", "Fulfilled"],
            report.ByCatalogItem.Select(r => new object?[] { r.Label, r.Created, r.Resolved }));
    }

    /// <summary>
    /// Checks the window is one the system can answer, and demands the view permission.
    /// <para>
    /// A period ending in the future is clamped rather than refused: "this month" run on the
    /// eighth is a reasonable thing to ask for, and the answer is the eight days that exist.
    /// </para>
    /// </summary>
    private ReportPeriod Validate(ReportPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);
        _currentUser.DemandPermission(Permissions.ReportView);

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        var to = period.To > today ? today : period.To;

        if (period.From > to)
        {
            throw new DomainException(
                "report.invalid_period",
                "The report period starts after it ends.");
        }

        if (to.DayNumber - period.From.DayNumber >= MaxPeriodDays)
        {
            throw new DomainException(
                "report.period_too_long",
                $"A report covers at most {MaxPeriodDays} days. Narrow the range.");
        }

        return new ReportPeriod { From = period.From, To = to, GroupId = period.GroupId };
    }
}
