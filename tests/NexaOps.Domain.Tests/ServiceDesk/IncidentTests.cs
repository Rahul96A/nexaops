using NexaOps.Domain.Common;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Tests.ServiceDesk;

/// <summary>
/// The incident state machine and aggregate invariants.
/// <para>
/// These rules are enforced in the domain rather than in a service, so that the REST API, the
/// workflow engine and a confirmed AI action all obey the same ones. Testing them here means
/// testing them once for every caller.
/// </para>
/// </summary>
public sealed class IncidentStateMachineTests
{
    [Theory]
    [InlineData(IncidentStatus.New, IncidentStatus.Assigned)]
    [InlineData(IncidentStatus.New, IncidentStatus.InProgress)]
    [InlineData(IncidentStatus.New, IncidentStatus.Cancelled)]
    [InlineData(IncidentStatus.Assigned, IncidentStatus.InProgress)]
    [InlineData(IncidentStatus.InProgress, IncidentStatus.Pending)]
    [InlineData(IncidentStatus.Pending, IncidentStatus.InProgress)]
    [InlineData(IncidentStatus.InProgress, IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Resolved, IncidentStatus.Closed)]
    [InlineData(IncidentStatus.Resolved, IncidentStatus.InProgress)]
    public void Legal_transitions_are_allowed(IncidentStatus from, IncidentStatus to)
        => IncidentStateMachine.CanTransition(from, to).ShouldBeTrue();

    [Theory]
    [InlineData(IncidentStatus.New, IncidentStatus.Closed)]
    [InlineData(IncidentStatus.Assigned, IncidentStatus.Closed)]
    [InlineData(IncidentStatus.InProgress, IncidentStatus.Closed)]
    [InlineData(IncidentStatus.Closed, IncidentStatus.InProgress)]
    [InlineData(IncidentStatus.Closed, IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Cancelled, IncidentStatus.InProgress)]
    [InlineData(IncidentStatus.Pending, IncidentStatus.New)]
    public void Illegal_transitions_are_refused(IncidentStatus from, IncidentStatus to)
        => IncidentStateMachine.CanTransition(from, to).ShouldBeFalse();

    [Fact]
    public void An_incident_can_always_transition_to_its_current_status()
    {
        foreach (var status in Enum.GetValues<IncidentStatus>())
        {
            IncidentStateMachine.CanTransition(status, status).ShouldBeTrue();
        }
    }

    [Fact]
    public void Closed_and_cancelled_are_terminal()
    {
        IncidentStateMachine.IsTerminal(IncidentStatus.Closed).ShouldBeTrue();
        IncidentStateMachine.IsTerminal(IncidentStatus.Cancelled).ShouldBeTrue();

        IncidentStateMachine.AllowedTransitionsFrom(IncidentStatus.Closed).ShouldBeEmpty();
        IncidentStateMachine.AllowedTransitionsFrom(IncidentStatus.Cancelled).ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_pending_status_pauses_the_sla_clock()
    {
        IncidentStateMachine.PausesSla(IncidentStatus.Pending).ShouldBeTrue();

        foreach (var status in Enum.GetValues<IncidentStatus>().Where(s => s != IncidentStatus.Pending))
        {
            IncidentStateMachine.PausesSla(status).ShouldBeFalse();
        }
    }

    [Fact]
    public void Open_statuses_are_the_ones_still_consuming_agent_capacity()
    {
        IncidentStateMachine.IsOpen(IncidentStatus.New).ShouldBeTrue();
        IncidentStateMachine.IsOpen(IncidentStatus.Assigned).ShouldBeTrue();
        IncidentStateMachine.IsOpen(IncidentStatus.InProgress).ShouldBeTrue();
        IncidentStateMachine.IsOpen(IncidentStatus.Pending).ShouldBeTrue();

        IncidentStateMachine.IsOpen(IncidentStatus.Resolved).ShouldBeFalse();
        IncidentStateMachine.IsOpen(IncidentStatus.Closed).ShouldBeFalse();
        IncidentStateMachine.IsOpen(IncidentStatus.Cancelled).ShouldBeFalse();
    }

    [Fact]
    public void An_illegal_transition_throws_with_a_machine_readable_code()
    {
        var error = Should.Throw<DomainException>(() =>
            IncidentStateMachine.EnsureCanTransition(IncidentStatus.New, IncidentStatus.Closed));

        error.Code.ShouldBe("incident.invalid_transition");
    }
}

/// <summary>Behaviour of the incident aggregate itself.</summary>
public sealed class IncidentAggregateTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    private static Incident NewIncident(IncidentStatus status = IncidentStatus.New) => new()
    {
        TenantId = Guid.NewGuid(),
        Number = "INC0000001",
        Title = "Cannot connect to the VPN",
        Description = "The VPN client reports authentication failed.",
        RequesterId = Guid.NewGuid(),
        Status = status
    };

    [Fact]
    public void Resolving_requires_a_resolution_code_and_notes()
    {
        var incident = NewIncident(IncidentStatus.InProgress);

        var error = Should.Throw<DomainException>(() =>
            incident.TransitionTo(IncidentStatus.Resolved, Actor, Now));

        error.Code.ShouldBe("incident.resolution_required");
        incident.Status.ShouldBe(IncidentStatus.InProgress);
    }

    [Fact]
    public void Resolving_records_who_resolved_it_and_when()
    {
        var incident = NewIncident(IncidentStatus.InProgress);
        incident.ResolutionCode = ResolutionCode.Resolved;
        incident.ResolutionNotes = "Upgraded the VPN client to 4.10.2.";

        incident.TransitionTo(IncidentStatus.Resolved, Actor, Now);

        incident.Status.ShouldBe(IncidentStatus.Resolved);
        incident.ResolvedAt.ShouldBe(Now);
        incident.ResolvedByUserId.ShouldBe(Actor);
    }

    [Fact]
    public void An_incident_must_be_resolved_before_it_can_be_closed()
    {
        var incident = NewIncident(IncidentStatus.InProgress);

        Should.Throw<DomainException>(() => incident.TransitionTo(IncidentStatus.Closed, Actor, Now))
            .Code.ShouldBe("incident.invalid_transition");
    }

    [Fact]
    public void Reopening_clears_the_resolution_and_counts_the_reopen()
    {
        var incident = NewIncident(IncidentStatus.InProgress);
        incident.ResolutionCode = ResolutionCode.Resolved;
        incident.ResolutionNotes = "Replaced the power adapter.";
        incident.TransitionTo(IncidentStatus.Resolved, Actor, Now);

        incident.TransitionTo(IncidentStatus.InProgress, Actor, Now.AddHours(2));

        incident.Status.ShouldBe(IncidentStatus.InProgress);
        incident.ReopenCount.ShouldBe(1);
        incident.ResolvedAt.ShouldBeNull();
        incident.ResolutionCode.ShouldBeNull();
        incident.ResolutionNotes.ShouldBeNull();
    }

    [Fact]
    public void Leaving_the_pending_state_clears_the_pending_reason()
    {
        var incident = NewIncident(IncidentStatus.InProgress);
        incident.PendingReason = PendingReason.AwaitingRequester;
        incident.TransitionTo(IncidentStatus.Pending, Actor, Now);

        incident.TransitionTo(IncidentStatus.InProgress, Actor, Now.AddHours(1));

        incident.PendingReason.ShouldBeNull();
    }

    [Fact]
    public void Assigning_a_new_incident_to_a_person_advances_it_to_assigned()
    {
        var incident = NewIncident();

        incident.Assign(Guid.NewGuid(), Guid.NewGuid());

        incident.Status.ShouldBe(IncidentStatus.Assigned);
    }

    [Fact]
    public void Assigning_to_a_group_alone_leaves_a_new_incident_in_the_pool()
    {
        var incident = NewIncident();

        incident.Assign(Guid.NewGuid(), null);

        incident.Status.ShouldBe(IncidentStatus.New);
        incident.AssignedToUserId.ShouldBeNull();
    }

    [Fact]
    public void A_terminal_incident_cannot_be_reassigned()
    {
        var incident = NewIncident(IncidentStatus.Closed);

        Should.Throw<DomainException>(() => incident.Assign(Guid.NewGuid(), Guid.NewGuid()))
            .Code.ShouldBe("incident.assign_terminal");
    }

    [Fact]
    public void First_response_is_recorded_once_and_never_moved()
    {
        var incident = NewIncident();

        incident.RecordFirstResponse(Now);
        incident.RecordFirstResponse(Now.AddHours(3));

        // The response commitment measures time to first contact, not most recent contact.
        incident.FirstRespondedAt.ShouldBe(Now);
    }

    [Fact]
    public void A_derived_priority_does_not_overwrite_a_deliberate_override()
    {
        var incident = NewIncident();
        incident.OverridePriority(Priority.P1Critical, "Board demonstration is in one hour.");

        incident.ApplyDerivedPriority(Priority.P4Low);

        incident.Priority.ShouldBe(Priority.P1Critical);
        incident.IsPriorityOverridden.ShouldBeTrue();
    }

    [Fact]
    public void A_derived_priority_applies_when_nothing_has_been_overridden()
    {
        var incident = NewIncident();

        incident.ApplyDerivedPriority(Priority.P2High);

        incident.Priority.ShouldBe(Priority.P2High);
        incident.IsPriorityOverridden.ShouldBeFalse();
    }

    [Fact]
    public void Overriding_priority_without_a_reason_is_refused()
    {
        var incident = NewIncident();

        Should.Throw<DomainException>(() => incident.OverridePriority(Priority.P1Critical, "   "))
            .Code.ShouldBe("incident.override_reason_required");
    }
}
