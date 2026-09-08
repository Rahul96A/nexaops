using NexaOps.Domain.Changes;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Application.Reporting;

/// <summary>
/// The window a report covers, as whole dates in the tenant's timezone.
/// <para>
/// Dates rather than timestamps because that is how the question is asked — "last month", not
/// "the 720 hours ending now" — and because a report whose boundaries move as you read it cannot
/// be reconciled against the one somebody else ran an hour ago.
/// </para>
/// </summary>
public sealed class ReportPeriod
{
    /// <summary>First day included.</summary>
    public DateOnly From { get; set; }

    /// <summary>Last day included.</summary>
    public DateOnly To { get; set; }

    /// <summary>Optional narrowing to one assignment group.</summary>
    public Guid? GroupId { get; set; }
}

/// <summary>
/// A measured value together with what it was measured over.
/// <para>
/// Rates always carry their denominator. "94%" of seventeen tickets and "94%" of seventeen
/// hundred are different facts, and a dashboard that shows only the percentage invites the
/// smaller one to be read as the larger.
/// </para>
/// </summary>
/// <param name="Percent">Null when the denominator is zero — no rate exists, rather than 0%.</param>
public sealed record RateDto(int Numerator, int Denominator, double? Percent)
{
    public static RateDto Of(int numerator, int denominator) => new(
        numerator,
        denominator,
        denominator == 0 ? null : Math.Round(numerator * 100.0 / denominator, 1));
}

/// <param name="Date">The day.</param>
/// <param name="Created">Records raised that day.</param>
/// <param name="Resolved">Records resolved that day, whenever they were raised.</param>
public sealed record DailyVolumeDto(DateOnly Date, int Created, int Resolved);

/// <param name="Label">What the row is about — a priority, a category, a group.</param>
/// <param name="Created">Raised in the period.</param>
/// <param name="Resolved">Resolved in the period.</param>
/// <param name="Breached">Of those resolved, how many had breached a commitment.</param>
public sealed record BreakdownRowDto(
    string Label,
    Guid? Id,
    int Created,
    int Resolved,
    int Breached,
    RateDto BreachRate);

/// <param name="Mean">Arithmetic mean. Sensitive to a handful of forgotten tickets.</param>
/// <param name="Median">
/// The middle value. Reported alongside the mean rather than instead of it: where the two
/// disagree sharply, the difference is the finding.
/// </param>
/// <param name="Sample">How many resolved records the figures are computed from.</param>
public sealed record DurationStatsDto(TimeSpan? Mean, TimeSpan? Median, int Sample);

/// <summary>Service desk performance over a period.</summary>
/// <param name="StillOpen">
/// Open right now, not "open at the end of the period" — the database records no history of
/// status, so a backlog as at a past date cannot be reconstructed and is not claimed.
/// </param>
/// <param name="IsPartialPeriod">True when the window includes today, which is not a whole day.</param>
public sealed record ServiceDeskReportDto(
    DateOnly From,
    DateOnly To,
    bool IsPartialPeriod,
    int Created,
    int Resolved,
    int Reopened,
    int StillOpen,
    RateDto SlaAttainment,
    RateDto ReopenRate,
    DurationStatsDto TimeToResolve,
    IReadOnlyList<DailyVolumeDto> Daily,
    IReadOnlyList<BreakdownRowDto> ByPriority,
    IReadOnlyList<BreakdownRowDto> ByCategory,
    IReadOnlyList<BreakdownRowDto> ByGroup);

/// <param name="Target">Response, resolution or closure.</param>
/// <param name="Priority">Null for the all-priorities row.</param>
public sealed record SlaAttainmentRowDto(
    SlaTargetType Target,
    Priority? Priority,
    int Met,
    int Breached,
    RateDto Attainment);

/// <summary>
/// SLA attainment over a period, counted from settled clocks.
/// <para>
/// A clock still running is not evidence either way and is excluded. Cancelled clocks are
/// excluded too — a commitment withdrawn because the record was cancelled was neither met nor
/// missed, and counting it either way would move the number for a reason nobody chose.
/// </para>
/// </summary>
public sealed record SlaReportDto(
    DateOnly From,
    DateOnly To,
    RateDto Overall,
    IReadOnlyList<SlaAttainmentRowDto> Rows,
    int StillRunning,
    int Cancelled);

/// <param name="Outcome">How the change ended.</param>
public sealed record ChangeOutcomeRowDto(ChangeOutcome Outcome, int Count);

/// <summary>
/// Change delivery over a period, counted from changes reviewed in it.
/// <para>
/// Reviewed rather than implemented: the outcome is recorded at review, so an implemented change
/// nobody has reviewed yet has no outcome to count. Counting it as successful would be an
/// assumption dressed as a measurement.
/// </para>
/// </summary>
public sealed record ChangeReportDto(
    DateOnly From,
    DateOnly To,
    int Raised,
    int Reviewed,
    int AwaitingReview,
    RateDto SuccessRate,
    RateDto EmergencyShare,
    IReadOnlyList<ChangeOutcomeRowDto> Outcomes);

/// <summary>Service request delivery over a period.</summary>
public sealed record RequestReportDto(
    DateOnly From,
    DateOnly To,
    int Raised,
    int Fulfilled,
    int Cancelled,
    int AwaitingApproval,
    DurationStatsDto TimeToFulfil,
    IReadOnlyList<DailyVolumeDto> Daily,
    IReadOnlyList<BreakdownRowDto> ByCatalogItem);
