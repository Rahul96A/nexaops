using System.Text;
using Microsoft.AspNetCore.Mvc;
using NexaOps.Api.Authorization;
using NexaOps.Application.Reporting;
using NexaOps.Application.Security;

namespace NexaOps.Api.Controllers;

/// <summary>
/// Operational reporting over a period.
/// <para>
/// Every figure is computed from records the tenant holds. Nothing here is cached, pre-aggregated
/// or estimated: a report that disagrees with the list view behind it is worse than no report.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/reports")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class ReportsController : ControllerBase
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports) => _reports = reports;

    /// <summary>Incident volume, resolution time, SLA attainment and where the work comes from.</summary>
    [HttpGet("service-desk")]
    [RequiresPermission(Permissions.ReportView)]
    [ProducesResponseType(typeof(ServiceDeskReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ServiceDeskReportDto>> ServiceDesk(
        [FromQuery] ReportPeriodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _reports.GetServiceDeskAsync(request.ToPeriod(), cancellationToken));
    }

    /// <summary>
    /// SLA attainment, counted from clocks that finished in the period.
    /// <para>
    /// Clocks still running are reported separately rather than counted: one that has not
    /// finished is not evidence either way.
    /// </para>
    /// </summary>
    [HttpGet("sla")]
    [RequiresPermission(Permissions.ReportView)]
    [ProducesResponseType(typeof(SlaReportDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SlaReportDto>> Sla(
        [FromQuery] ReportPeriodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _reports.GetSlaAttainmentAsync(request.ToPeriod(), cancellationToken));
    }

    /// <summary>Change delivery, counted from changes reviewed in the period.</summary>
    [HttpGet("changes")]
    [RequiresPermission(Permissions.ReportView)]
    [ProducesResponseType(typeof(ChangeReportDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChangeReportDto>> Changes(
        [FromQuery] ReportPeriodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _reports.GetChangeDeliveryAsync(request.ToPeriod(), cancellationToken));
    }

    /// <summary>Service request delivery and what the catalogue is actually generating.</summary>
    [HttpGet("requests")]
    [RequiresPermission(Permissions.ReportView)]
    [ProducesResponseType(typeof(RequestReportDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RequestReportDto>> Requests(
        [FromQuery] ReportPeriodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _reports.GetRequestDeliveryAsync(request.ToPeriod(), cancellationToken));
    }

    /// <summary>
    /// Downloads a report as CSV.
    /// <para>
    /// A separate permission from viewing, and audited: a figure on a screen stays inside the
    /// application, and a file leaves with whoever downloaded it.
    /// </para>
    /// </summary>
    [HttpGet("{report}/export")]
    [RequiresPermission(Permissions.ReportExport)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Export(
        string report,
        [FromQuery] ReportPeriodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var export = await _reports.ExportAsync(report, request.ToPeriod(), cancellationToken);

        // UTF-8 with a BOM: without it Excel on Windows reads the file as the system codepage
        // and mangles every name that is not plain ASCII, which in an Indian deployment is most
        // of them.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(export.Content)).ToArray();

        return File(bytes, "text/csv", export.FileName);
    }
}

/// <summary>
/// Query-string binding for a report window.
/// <para>
/// Defaults to the last thirty days including today, because that is the question people ask
/// without thinking about it, and an unbounded default would be a full table scan on first load.
/// </para>
/// </summary>
public sealed class ReportPeriodRequest
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public Guid? GroupId { get; set; }

    public ReportPeriod ToPeriod()
    {
        var to = To ?? DateOnly.FromDateTime(DateTime.UtcNow);

        return new ReportPeriod
        {
            From = From ?? to.AddDays(-29),
            To = to,
            GroupId = GroupId
        };
    }
}
