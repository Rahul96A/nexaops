using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Reporting;
using NexaOps.Domain.Changes;
using NexaOps.Domain.Requests;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class ReportQueryService : IReportQueryService
{
    private readonly NexaOpsDbContext _context;
    private readonly IDateTimeProvider _clock;

    public ReportQueryService(NexaOpsDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<ServiceDeskReportDto> GetServiceDeskAsync(
        ReportPeriod period,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(period);

        var (from, to) = Bounds(period);

        var all = _context.Incidents.AsNoTracking();

        if (period.GroupId is not null)
        {
            all = all.Where(i => i.AssignmentGroupId == period.GroupId);
        }

        var created = all.Where(i => i.CreatedAt >= from && i.CreatedAt < to);
        var resolved = all.Where(i => i.ResolvedAt != null && i.ResolvedAt >= from && i.ResolvedAt < to);

        var createdCount = await created.CountAsync(ct).ConfigureAwait(false);
        var resolvedCount = await resolved.CountAsync(ct).ConfigureAwait(false);

        // Reopen count is a property of the incidents raised in the window, not of events in it:
        // the database records how many times an incident has been reopened, not when. The
        // report says "of the incidents raised in this period, this many have been reopened",
        // which is answerable, rather than "this many reopenings happened", which is not.
        var reopened = await created.CountAsync(i => i.ReopenCount > 0, ct).ConfigureAwait(false);

        // Open right now. A backlog as at a past date would need status history, which is not
        // stored, so it is not claimed.
        var stillOpen = await all
            .CountAsync(
                i => i.Status != IncidentStatus.Resolved
                     && i.Status != IncidentStatus.Closed
                     && i.Status != IncidentStatus.Cancelled,
                ct)
            .ConfigureAwait(false);

        var breachedOfResolved = await resolved.CountAsync(i => i.HasBreachedSla, ct).ConfigureAwait(false);

        var stats = await ResolutionStatsAsync(resolved, ct).ConfigureAwait(false);

        var daily = await DailyAsync(created, resolved, period, ct).ConfigureAwait(false);

        var byPriority = await created
            .GroupBy(i => i.Priority)
            .Select(g => new
            {
                g.Key,
                Created = g.Count(),
                Resolved = g.Count(i => i.ResolvedAt != null),
                Breached = g.Count(i => i.HasBreachedSla)
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byCategory = await created
            .GroupBy(i => new { i.CategoryId, Name = i.Category!.Name })
            .Select(g => new
            {
                g.Key.CategoryId,
                g.Key.Name,
                Created = g.Count(),
                Resolved = g.Count(i => i.ResolvedAt != null),
                Breached = g.Count(i => i.HasBreachedSla)
            })
            .OrderByDescending(r => r.Created)
            .Take(15)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byGroup = await created
            .GroupBy(i => new { i.AssignmentGroupId, Name = i.AssignmentGroup!.Name })
            .Select(g => new
            {
                g.Key.AssignmentGroupId,
                g.Key.Name,
                Created = g.Count(),
                Resolved = g.Count(i => i.ResolvedAt != null),
                Breached = g.Count(i => i.HasBreachedSla)
            })
            .OrderByDescending(r => r.Created)
            .Take(15)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        return new ServiceDeskReportDto(
            period.From,
            period.To,
            period.To >= today,
            createdCount,
            resolvedCount,
            reopened,
            stillOpen,

            // Attainment among resolved incidents: of the ones that finished in this window, how
            // many finished without breaching. Incidents still open are not evidence either way.
            RateDto.Of(resolvedCount - breachedOfResolved, resolvedCount),
            RateDto.Of(reopened, createdCount),
            stats,
            daily,
            [
                .. byPriority
                    .OrderBy(r => r.Key)
                    .Select(r => new BreakdownRowDto(
                        r.Key.ToString(), null, r.Created, r.Resolved, r.Breached,
                        RateDto.Of(r.Breached, r.Created)))
            ],
            [
                .. byCategory.Select(r => new BreakdownRowDto(
                    r.Name ?? "Uncategorised", r.CategoryId, r.Created, r.Resolved, r.Breached,
                    RateDto.Of(r.Breached, r.Created)))
            ],
            [
                .. byGroup.Select(r => new BreakdownRowDto(
                    r.Name ?? "Unassigned", r.AssignmentGroupId, r.Created, r.Resolved, r.Breached,
                    RateDto.Of(r.Breached, r.Created)))
            ]);
    }

    /// <inheritdoc />
    public async Task<SlaReportDto> GetSlaAttainmentAsync(ReportPeriod period, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(period);

        var (from, to) = Bounds(period);

        // Clocks that finished in the window. A clock still running is not evidence either way,
        // and a cancelled one was neither met nor missed.
        var settled = _context.SlaInstances
            .AsNoTracking()
            .Where(s => s.CompletedAt != null && s.CompletedAt >= from && s.CompletedAt < to)
            .Where(s => s.State == SlaState.Met || s.State == SlaState.Breached);

        var rows = await settled
            .GroupBy(s => s.TargetType)
            .Select(g => new
            {
                Target = g.Key,
                Met = g.Count(s => s.State == SlaState.Met),
                Breached = g.Count(s => s.State == SlaState.Breached)
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var stillRunning = await _context.SlaInstances
            .AsNoTracking()
            .CountAsync(s => s.State == SlaState.InProgress || s.State == SlaState.Paused, ct)
            .ConfigureAwait(false);

        var cancelled = await _context.SlaInstances
            .AsNoTracking()
            .CountAsync(
                s => s.State == SlaState.Cancelled
                     && s.CompletedAt != null && s.CompletedAt >= from && s.CompletedAt < to,
                ct)
            .ConfigureAwait(false);

        var totalMet = rows.Sum(r => r.Met);
        var totalBreached = rows.Sum(r => r.Breached);

        return new SlaReportDto(
            period.From,
            period.To,
            RateDto.Of(totalMet, totalMet + totalBreached),
            [
                .. rows
                    .OrderBy(r => r.Target)
                    .Select(r => new SlaAttainmentRowDto(
                        r.Target, null, r.Met, r.Breached, RateDto.Of(r.Met, r.Met + r.Breached)))
            ],
            stillRunning,
            cancelled);
    }

    /// <inheritdoc />
    public async Task<ChangeReportDto> GetChangeDeliveryAsync(ReportPeriod period, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(period);

        var (from, to) = Bounds(period);

        var changes = _context.Changes.AsNoTracking();

        if (period.GroupId is not null)
        {
            changes = changes.Where(c => c.AssignmentGroupId == period.GroupId);
        }

        var raised = changes.Where(c => c.CreatedAt >= from && c.CreatedAt < to);

        // Reviewed, not implemented: the outcome is recorded at review, so an implemented change
        // nobody has reviewed has no outcome. Counting it as successful would be an assumption
        // presented as a measurement.
        var reviewed = changes.Where(c => c.ReviewedAt != null && c.ReviewedAt >= from && c.ReviewedAt < to);

        var raisedCount = await raised.CountAsync(ct).ConfigureAwait(false);
        var emergency = await raised.CountAsync(c => c.Type == ChangeType.Emergency, ct).ConfigureAwait(false);

        var awaitingReview = await changes
            .CountAsync(c => c.Status == ChangeStatus.Review, ct)
            .ConfigureAwait(false);

        var outcomes = await reviewed
            .Where(c => c.Outcome != null)
            .GroupBy(c => c.Outcome!.Value)
            .Select(g => new { Outcome = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var reviewedCount = outcomes.Sum(o => o.Count);

        // Success counts a change that worked. "Successful with issues" is deliberately not
        // counted as success: a change that overran its window or caused an unplanned side
        // effect is exactly what a change process exists to reduce, and folding it into the
        // headline number would hide the thing being measured.
        var successful = outcomes
            .Where(o => o.Outcome == ChangeOutcome.Successful)
            .Sum(o => o.Count);

        return new ChangeReportDto(
            period.From,
            period.To,
            raisedCount,
            reviewedCount,
            awaitingReview,
            RateDto.Of(successful, reviewedCount),
            RateDto.Of(emergency, raisedCount),
            [.. outcomes.OrderBy(o => o.Outcome).Select(o => new ChangeOutcomeRowDto(o.Outcome, o.Count))]);
    }

    /// <inheritdoc />
    public async Task<RequestReportDto> GetRequestDeliveryAsync(ReportPeriod period, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(period);

        var (from, to) = Bounds(period);

        var requests = _context.ServiceRequests.AsNoTracking();

        if (period.GroupId is not null)
        {
            requests = requests.Where(r => r.FulfilmentGroupId == period.GroupId);
        }

        var raised = requests.Where(r => r.CreatedAt >= from && r.CreatedAt < to);
        var fulfilled = requests.Where(r => r.FulfilledAt != null && r.FulfilledAt >= from && r.FulfilledAt < to);

        var raisedCount = await raised.CountAsync(ct).ConfigureAwait(false);
        var fulfilledCount = await fulfilled.CountAsync(ct).ConfigureAwait(false);

        var cancelled = await raised
            .CountAsync(r => r.Status == RequestStatus.Cancelled, ct)
            .ConfigureAwait(false);

        var awaitingApproval = await requests
            .CountAsync(r => r.Status == RequestStatus.AwaitingApproval, ct)
            .ConfigureAwait(false);

        var stats = await FulfilmentStatsAsync(fulfilled, ct).ConfigureAwait(false);

        var daily = await DailyRequestsAsync(raised, fulfilled, period, ct).ConfigureAwait(false);

        // Grouped by the ordered line, because "which of our catalogue items generate the work"
        // is the question a catalogue owner asks; the request itself may carry several.
        var byItem = await _context.RequestItems
            .AsNoTracking()
            .Where(i => i.CreatedAt >= from && i.CreatedAt < to)
            .GroupBy(i => i.CatalogItemName)
            .Select(g => new
            {
                Name = g.Key,
                Created = g.Count(),
                Delivered = g.Count(i => i.Status == RequestItemStatus.Fulfilled)
            })
            .OrderByDescending(r => r.Created)
            .Take(15)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new RequestReportDto(
            period.From,
            period.To,
            raisedCount,
            fulfilledCount,
            cancelled,
            awaitingApproval,
            stats,
            daily,
            [
                .. byItem.Select(r => new BreakdownRowDto(
                    r.Name, null, r.Created, r.Delivered, 0, RateDto.Of(0, r.Created)))
            ]);
    }

    /// <summary>
    /// Mean and median time to resolve, over the incidents resolved in the window.
    /// <para>
    /// Both, not one. The mean of ticket durations is dominated by the handful nobody closed;
    /// the median hides a tail that is somebody's whole month. Where the two disagree sharply,
    /// the disagreement is the finding, and reporting only one conceals it.
    /// </para>
    /// </summary>
    private static async Task<DurationStatsDto> ResolutionStatsAsync(
        IQueryable<Incident> resolved,
        CancellationToken ct)
    {
        var durations = resolved.Select(i => EF.Functions.DateDiffMinute(i.CreatedAt, i.ResolvedAt!.Value));

        var sample = await durations.CountAsync(ct).ConfigureAwait(false);

        if (sample == 0)
        {
            // No resolutions in the window. There is no average, which is not the same as zero.
            return new DurationStatsDto(null, null, 0);
        }

        var mean = await durations.AverageAsync(ct).ConfigureAwait(false);

        // The middle row, fetched by ordering and skipping rather than pulling the set into
        // memory. For an even sample this is the upper of the two middle values, which is close
        // enough for an operational report and avoids a second round trip to average them.
        var median = await durations
            .OrderBy(d => d)
            .Skip(sample / 2)
            .FirstAsync(ct)
            .ConfigureAwait(false);

        return new DurationStatsDto(
            TimeSpan.FromMinutes(mean),
            TimeSpan.FromMinutes(median),
            sample);
    }

    private static async Task<DurationStatsDto> FulfilmentStatsAsync(
        IQueryable<ServiceRequest> fulfilled,
        CancellationToken ct)
    {
        var durations = fulfilled.Select(r => EF.Functions.DateDiffMinute(r.CreatedAt, r.FulfilledAt!.Value));

        var sample = await durations.CountAsync(ct).ConfigureAwait(false);

        if (sample == 0)
        {
            return new DurationStatsDto(null, null, 0);
        }

        var mean = await durations.AverageAsync(ct).ConfigureAwait(false);

        var median = await durations
            .OrderBy(d => d)
            .Skip(sample / 2)
            .FirstAsync(ct)
            .ConfigureAwait(false);

        return new DurationStatsDto(TimeSpan.FromMinutes(mean), TimeSpan.FromMinutes(median), sample);
    }

    /// <summary>
    /// Created and resolved per day, with every day in the window present.
    /// <para>
    /// Days with no activity are returned as zeros rather than omitted. A chart that skips empty
    /// days compresses a quiet week into a busy-looking line.
    /// </para>
    /// </summary>
    private static async Task<IReadOnlyList<DailyVolumeDto>> DailyAsync(
        IQueryable<Incident> created,
        IQueryable<Incident> resolved,
        ReportPeriod period,
        CancellationToken ct)
    {
        var createdByDay = await created
            .GroupBy(i => i.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var resolvedByDay = await resolved
            .GroupBy(i => i.ResolvedAt!.Value.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Series(period, createdByDay.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count),
            resolvedByDay.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count));
    }

    private static async Task<IReadOnlyList<DailyVolumeDto>> DailyRequestsAsync(
        IQueryable<ServiceRequest> raised,
        IQueryable<ServiceRequest> fulfilled,
        ReportPeriod period,
        CancellationToken ct)
    {
        var raisedByDay = await raised
            .GroupBy(r => r.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var fulfilledByDay = await fulfilled
            .GroupBy(r => r.FulfilledAt!.Value.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Series(period, raisedByDay.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count),
            fulfilledByDay.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count));
    }

    private static List<DailyVolumeDto> Series(
        ReportPeriod period,
        Dictionary<DateOnly, int> opened,
        Dictionary<DateOnly, int> closed)
    {
        var series = new List<DailyVolumeDto>();

        for (var day = period.From; day <= period.To; day = day.AddDays(1))
        {
            opened.TryGetValue(day, out var openedCount);
            closed.TryGetValue(day, out var closedCount);
            series.Add(new DailyVolumeDto(day, openedCount, closedCount));
        }

        return series;
    }

    /// <summary>
    /// The period as a half-open instant range.
    /// <para>
    /// The end is exclusive and one day past the last date, so a record created at 23:59 on the
    /// final day is inside the report. An inclusive comparison against midnight would silently
    /// drop the busiest hours of the last day.
    /// </para>
    /// </summary>
    private static (DateTimeOffset From, DateTimeOffset To) Bounds(ReportPeriod period) => (
        new DateTimeOffset(period.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        new DateTimeOffset(period.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
}
