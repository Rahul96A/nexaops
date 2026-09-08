using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Domain.Tests.Sla;

/// <summary>
/// The behaviour of one live SLA clock: pausing, resuming, meeting and breaching.
/// <para>
/// The rules that matter commercially are that paused time is credited back to the customer,
/// and that a breach is dated to the deadline rather than to the moment the monitor happened
/// to notice.
/// </para>
/// </summary>
public sealed class SlaInstanceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 7, 4, 0, 0, TimeSpan.Zero);

    /// <summary>24x7, so the arithmetic under test is the clock's, not the calendar's.</summary>
    private static readonly BusinessSchedule AlwaysOn = BusinessSchedule.TwentyFourSeven();

    private static SlaInstance FourHourClock() => new()
    {
        TenantId = Guid.NewGuid(),
        Module = ServiceModule.Incident,
        RecordId = Guid.NewGuid(),
        TargetType = SlaTargetType.Resolution,
        SlaName = "P1 critical resolution",
        DurationMinutes = 240,
        StartedAt = Start,
        DueAt = Start.AddMinutes(240),
        State = SlaState.InProgress
    };

    [Fact]
    public void Completing_before_the_deadline_meets_the_commitment()
    {
        var clock = FourHourClock();

        clock.Complete(Start.AddMinutes(90));

        clock.State.ShouldBe(SlaState.Met);
        clock.BreachedAt.ShouldBeNull();
        clock.IsSettled.ShouldBeTrue();
    }

    [Fact]
    public void Completing_after_the_deadline_is_a_breach_dated_to_the_deadline()
    {
        var clock = FourHourClock();

        clock.Complete(Start.AddMinutes(400));

        clock.State.ShouldBe(SlaState.Breached);
        clock.BreachedAt.ShouldBe(clock.DueAt);
    }

    [Fact]
    public void A_running_clock_past_its_deadline_breaches_at_the_deadline_not_at_detection()
    {
        var clock = FourHourClock();

        // The monitor happens to run three hours late; that must not distort reporting.
        clock.MarkBreachedIfOverdue(Start.AddMinutes(420)).ShouldBeTrue();

        clock.BreachedAt.ShouldBe(clock.DueAt);
        clock.State.ShouldBe(SlaState.Breached);
    }

    [Fact]
    public void A_running_clock_inside_its_deadline_does_not_breach()
    {
        var clock = FourHourClock();

        clock.MarkBreachedIfOverdue(Start.AddMinutes(120)).ShouldBeFalse();
        clock.State.ShouldBe(SlaState.InProgress);
    }

    [Fact]
    public void A_paused_clock_cannot_breach_while_it_is_stopped()
    {
        var clock = FourHourClock();
        clock.Pause(Start.AddMinutes(60));

        // Waiting on the requester is not time the service desk is accountable for, so the
        // commitment must not tip into breach while the clock is stopped.
        clock.MarkBreachedIfOverdue(Start.AddDays(3)).ShouldBeFalse();

        clock.State.ShouldBe(SlaState.Paused);
        clock.BreachedAt.ShouldBeNull();
    }

    [Fact]
    public void Resuming_pushes_the_deadline_out_by_the_paused_time()
    {
        var clock = FourHourClock();
        var originalDue = clock.DueAt;

        clock.Pause(Start.AddMinutes(60));
        clock.Resume(AlwaysOn, Start.AddMinutes(180));

        clock.State.ShouldBe(SlaState.InProgress);
        clock.PausedAt.ShouldBeNull();
        clock.PausedBusinessMinutes.ShouldBe(120);
        clock.DueAt.ShouldBe(originalDue.AddMinutes(120));
    }

    [Fact]
    public void Elapsed_time_excludes_the_paused_period()
    {
        var clock = FourHourClock();

        clock.Pause(Start.AddMinutes(60));
        clock.Resume(AlwaysOn, Start.AddMinutes(180));

        // Three hours of wall clock, two of which were spent waiting on someone else.
        clock.ElapsedBusinessMinutes(AlwaysOn, Start.AddMinutes(180)).ShouldBe(60);
    }

    [Fact]
    public void Elapsed_time_on_a_paused_clock_is_measured_to_the_moment_it_paused()
    {
        var clock = FourHourClock();
        clock.Pause(Start.AddMinutes(45));

        clock.ElapsedBusinessMinutes(AlwaysOn, Start.AddDays(2)).ShouldBe(45);
    }

    [Fact]
    public void Remaining_time_goes_negative_once_the_commitment_is_overrun()
    {
        var clock = FourHourClock();

        clock.RemainingBusinessMinutes(AlwaysOn, Start.AddMinutes(180)).ShouldBe(60);
        clock.RemainingBusinessMinutes(AlwaysOn, Start.AddMinutes(300)).ShouldBe(-60);
    }

    [Fact]
    public void Consumed_percentage_tracks_the_commitment()
    {
        var clock = FourHourClock();

        clock.ConsumedPercent(AlwaysOn, Start).ShouldBe(0);
        clock.ConsumedPercent(AlwaysOn, Start.AddMinutes(120)).ShouldBe(50);
        clock.ConsumedPercent(AlwaysOn, Start.AddMinutes(240)).ShouldBe(100);

        // Overrun is reported honestly rather than pinned at 100.
        clock.ConsumedPercent(AlwaysOn, Start.AddMinutes(480)).ShouldBe(200);
    }

    [Fact]
    public void A_clock_that_was_already_breached_stays_breached_after_resuming()
    {
        var clock = FourHourClock();

        clock.MarkBreachedIfOverdue(Start.AddMinutes(300));
        clock.Pause(Start.AddMinutes(310));   // no effect: the clock is settled
        clock.Resume(AlwaysOn, Start.AddMinutes(400));

        clock.State.ShouldBe(SlaState.Breached);
    }

    [Fact]
    public void Cancelling_settles_the_clock_without_an_outcome()
    {
        var clock = FourHourClock();

        clock.Cancel();

        clock.State.ShouldBe(SlaState.Cancelled);
        clock.IsSettled.ShouldBeTrue();
        clock.BreachedAt.ShouldBeNull();
    }

    [Fact]
    public void A_settled_clock_ignores_further_transitions()
    {
        var clock = FourHourClock();
        clock.Complete(Start.AddMinutes(30));

        clock.Cancel();
        clock.Pause(Start.AddMinutes(40));
        clock.Complete(Start.AddMinutes(500));

        clock.State.ShouldBe(SlaState.Met);
        clock.CompletedAt.ShouldBe(Start.AddMinutes(30));
    }

    [Fact]
    public void A_clock_with_no_duration_reports_zero_consumption_rather_than_dividing_by_zero()
    {
        var clock = FourHourClock();
        clock.DurationMinutes = 0;

        clock.ConsumedPercent(AlwaysOn, Start.AddMinutes(60)).ShouldBe(0);
    }
}
