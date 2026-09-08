using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Sla;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Application.Tests.Sla;

/// <summary>
/// How SLA clocks follow the incident lifecycle.
/// <para>
/// The persistence ports are substituted so these exercise the orchestration decisions - which
/// policy wins, when a clock pauses, what happens on a priority change - rather than the
/// database. The clock arithmetic itself is covered by the domain tests.
/// </para>
/// </summary>
public sealed class SlaServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 6, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    private readonly ISlaRepository _repository = Substitute.For<ISlaRepository>();
    private readonly ISlaInstanceWriter _writer = Substitute.For<ISlaInstanceWriter>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly SlaService _service;

    public SlaServiceTests()
    {
        _clock.UtcNow.Returns(Now);

        _repository.GetScheduleAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(BusinessSchedule.TwentyFourSeven());

        _service = new SlaService(_repository, _writer, _clock, NullLogger<SlaService>.Instance);
    }

    private static SlaDefinition Definition(
        SlaTargetType targetType,
        int minutes,
        string name,
        bool pauseWhenPending = true) => new()
        {
            TenantId = TenantId,
            Code = $"INC-{name}",
            Name = name,
            Module = ServiceModule.Incident,
            TargetType = targetType,
            DurationMinutes = minutes,
            PauseWhenPending = pauseWhenPending,
            IsActive = true
        };

    private static SlaPolicy Policy(SlaDefinition definition, Priority? priority, int order) => new()
    {
        TenantId = TenantId,
        Name = definition.Name,
        SlaDefinitionId = definition.Id,
        SlaDefinition = definition,
        Module = ServiceModule.Incident,
        Priority = priority,
        Order = order,
        IsActive = true
    };

    private void WithPolicies(params SlaPolicy[] policies)
        => _repository.GetActivePoliciesAsync(ServiceModule.Incident, Arg.Any<CancellationToken>())
            .Returns(policies.ToList());

    private static Incident Incident(
        Priority priority = Priority.P2High,
        IncidentStatus status = IncidentStatus.New) => new()
        {
            TenantId = TenantId,
            Number = "INC0000001",
            Title = "Cannot reach the ERP system",
            Description = "The ERP login page times out.",
            RequesterId = Guid.NewGuid(),
            Priority = priority,
            Status = status,
            CreatedAt = Now
        };

    [Fact]
    public async Task Attaching_creates_a_response_and_a_resolution_clock()
    {
        var incident = Incident();
        WithPolicies(
            Policy(Definition(SlaTargetType.Response, 30, "P2 response"), Priority.P2High, 10),
            Policy(Definition(SlaTargetType.Resolution, 480, "P2 resolution"), Priority.P2High, 20));

        await _service.AttachClocksAsync(incident);

        incident.SlaInstances.Count.ShouldBe(2);
        incident.SlaInstances.ShouldContain(i => i.TargetType == SlaTargetType.Response);
        incident.SlaInstances.ShouldContain(i => i.TargetType == SlaTargetType.Resolution);

        _writer.Received(2).AddSlaInstance(Arg.Any<SlaInstance>());
    }

    [Fact]
    public async Task The_due_date_is_the_start_plus_the_commitment()
    {
        var incident = Incident();
        WithPolicies(Policy(Definition(SlaTargetType.Resolution, 480, "P2 resolution"), Priority.P2High, 10));

        await _service.AttachClocksAsync(incident);

        var clock = incident.SlaInstances.Single();
        clock.DueAt.ShouldBe(Now.AddMinutes(480));
        clock.StartedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task The_first_matching_policy_in_order_wins()
    {
        var incident = Incident(Priority.P1Critical);

        WithPolicies(
            // A specific P1 policy sits above the catch-all; the catch-all is not deleted, and
            // must not win simply because it also matches.
            Policy(Definition(SlaTargetType.Resolution, 240, "P1 resolution"), Priority.P1Critical, 10),
            Policy(Definition(SlaTargetType.Resolution, 1440, "Default resolution"), null, 99));

        await _service.AttachClocksAsync(incident);

        incident.SlaInstances.Single().SlaName.ShouldBe("P1 resolution");
    }

    [Fact]
    public async Task Attaching_twice_does_not_duplicate_a_live_clock()
    {
        var incident = Incident();
        WithPolicies(Policy(Definition(SlaTargetType.Resolution, 480, "P2 resolution"), Priority.P2High, 10));

        await _service.AttachClocksAsync(incident);
        await _service.AttachClocksAsync(incident);

        incident.SlaInstances.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_incident_with_no_matching_policy_carries_no_commitment()
    {
        var incident = Incident(Priority.P4Low);
        WithPolicies(Policy(Definition(SlaTargetType.Resolution, 240, "P1 resolution"), Priority.P1Critical, 10));

        await _service.AttachClocksAsync(incident);

        incident.SlaInstances.ShouldBeEmpty();
        incident.NextSlaDueAt.ShouldBeNull();
    }

    [Fact]
    public async Task Moving_to_pending_pauses_the_resolution_clock_but_not_the_response_clock()
    {
        var incident = Incident();
        WithPolicies(
            Policy(Definition(SlaTargetType.Response, 30, "P2 response"), Priority.P2High, 10),
            Policy(Definition(SlaTargetType.Resolution, 480, "P2 resolution"), Priority.P2High, 20));

        await _service.AttachClocksAsync(incident);
        incident.Status = IncidentStatus.Pending;

        await _service.OnStatusChangedAsync(incident, IncidentSlaMapping.For(incident, IncidentStatus.InProgress));

        var response = incident.SlaInstances.Single(i => i.TargetType == SlaTargetType.Response);
        var resolution = incident.SlaInstances.Single(i => i.TargetType == SlaTargetType.Resolution);

        // Waiting on the requester before anyone has replied is still the desk's silence.
        response.State.ShouldBe(SlaState.InProgress);
        resolution.State.ShouldBe(SlaState.Paused);
    }

    [Fact]
    public async Task A_definition_that_opts_out_of_pausing_keeps_running_while_pending()
    {
        var incident = Incident();
        WithPolicies(Policy(
            Definition(SlaTargetType.Resolution, 480, "P2 resolution", pauseWhenPending: false),
            Priority.P2High,
            10));

        await _service.AttachClocksAsync(incident);
        incident.Status = IncidentStatus.Pending;

        await _service.OnStatusChangedAsync(incident, IncidentSlaMapping.For(incident, IncidentStatus.InProgress));

        incident.SlaInstances.Single().State.ShouldBe(SlaState.InProgress);
    }

    [Fact]
    public async Task Leaving_pending_resumes_the_clock_and_credits_the_paused_time()
    {
        var incident = Incident();
        WithPolicies(Policy(Definition(SlaTargetType.Resolution, 480, "P2 resolution"), Priority.P2High, 10));

        await _service.AttachClocksAsync(incident);
        var originalDue = incident.SlaInstances.Single().DueAt;

        incident.Status = IncidentStatus.Pending;
        await _service.OnStatusChangedAsync(incident, IncidentSlaMapping.For(incident, IncidentStatus.InProgress));

        // Two hours pass while the desk waits on the requester.
        _clock.UtcNow.Returns(Now.AddMinutes(120));
        incident.Status = IncidentStatus.InProgress;
        await _service.OnStatusChangedAsync(incident, IncidentSlaMapping.For(incident, IncidentStatus.Pending));

        var clock = incident.SlaInstances.Single();
        clock.State.ShouldBe(SlaState.InProgress);
        clock.DueAt.ShouldBe(originalDue.AddMinutes(120));
    }

    [Fact]
    public async Task Resolving_completes_the_resolution_clock()
    {
        var incident = Incident();
        WithPolicies(Policy(Definition(SlaTargetType.Resolution, 480, "P2 resolution"), Priority.P2High, 10));

        await _service.AttachClocksAsync(incident);

        incident.Status = IncidentStatus.Resolved;
        incident.ResolvedAt = Now.AddMinutes(120);
        await _service.OnStatusChangedAsync(incident, IncidentSlaMapping.For(incident, IncidentStatus.InProgress));

        var clock = incident.SlaInstances.Single();
        clock.State.ShouldBe(SlaState.Met);
        clock.CompletedAt.ShouldBe(Now.AddMinutes(120));
    }

    [Fact]
    public async Task Resolving_late_records_a_breach()
    {
        var incident = Incident();
        WithPolicies(Policy(Definition(SlaTargetType.Resolution, 480, "P2 resolution"), Priority.P2High, 10));

        await _service.AttachClocksAsync(incident);

        incident.Status = IncidentStatus.Resolved;
        incident.ResolvedAt = Now.AddMinutes(600);
        await _service.OnStatusChangedAsync(incident, IncidentSlaMapping.For(incident, IncidentStatus.InProgress));

        incident.SlaInstances.Single().State.ShouldBe(SlaState.Breached);
        incident.HasBreachedSla.ShouldBeTrue();
    }

    [Fact]
    public async Task Cancelling_an_incident_cancels_its_commitments()
    {
        var incident = Incident();
        WithPolicies(Policy(Definition(SlaTargetType.Resolution, 480, "P2 resolution"), Priority.P2High, 10));

        await _service.AttachClocksAsync(incident);

        incident.Status = IncidentStatus.Cancelled;
        await _service.OnStatusChangedAsync(incident, IncidentSlaMapping.For(incident, IncidentStatus.InProgress));

        incident.SlaInstances.Single().State.ShouldBe(SlaState.Cancelled);
        incident.NextSlaDueAt.ShouldBeNull();
    }

    [Fact]
    public async Task The_first_agent_response_completes_the_response_clock()
    {
        var incident = Incident();
        WithPolicies(
            Policy(Definition(SlaTargetType.Response, 30, "P2 response"), Priority.P2High, 10),
            Policy(Definition(SlaTargetType.Resolution, 480, "P2 resolution"), Priority.P2High, 20));

        await _service.AttachClocksAsync(incident);

        incident.RecordFirstResponse(Now.AddMinutes(12));
        await _service.OnFirstResponseAsync(incident, incident.FirstRespondedAt!.Value);

        incident.SlaInstances.Single(i => i.TargetType == SlaTargetType.Response)
            .State.ShouldBe(SlaState.Met);

        // The resolution commitment is untouched by a first reply.
        incident.SlaInstances.Single(i => i.TargetType == SlaTargetType.Resolution)
            .State.ShouldBe(SlaState.InProgress);
    }

    [Fact]
    public async Task Raising_the_priority_re_targets_a_running_clock_from_its_original_start()
    {
        var incident = Incident(Priority.P3Moderate);

        WithPolicies(
            Policy(Definition(SlaTargetType.Resolution, 240, "P1 resolution"), Priority.P1Critical, 10),
            Policy(Definition(SlaTargetType.Resolution, 1440, "P3 resolution"), Priority.P3Moderate, 20));

        await _service.AttachClocksAsync(incident);
        incident.SlaInstances.Single().DueAt.ShouldBe(Now.AddMinutes(1440));

        // Escalated to P1 an hour later.
        _clock.UtcNow.Returns(Now.AddMinutes(60));
        incident.Priority = Priority.P1Critical;
        await _service.OnPriorityChangedAsync(incident);

        var clock = incident.SlaInstances.Single();
        clock.SlaName.ShouldBe("P1 resolution");

        // Time already spent still counts against the tighter commitment, so the deadline is
        // four hours from creation - not four hours from the escalation.
        clock.DueAt.ShouldBe(Now.AddMinutes(240));
    }

    [Fact]
    public async Task Lowering_the_priority_can_lift_a_clock_back_out_of_breach()
    {
        var incident = Incident(Priority.P1Critical);

        WithPolicies(
            Policy(Definition(SlaTargetType.Resolution, 240, "P1 resolution"), Priority.P1Critical, 10),
            Policy(Definition(SlaTargetType.Resolution, 1440, "P3 resolution"), Priority.P3Moderate, 20));

        await _service.AttachClocksAsync(incident);

        // Five hours in, the P1 commitment is blown.
        _clock.UtcNow.Returns(Now.AddMinutes(300));
        incident.SlaInstances.Single().MarkBreachedIfOverdue(Now.AddMinutes(300));
        incident.SlaInstances.Single().State.ShouldBe(SlaState.Breached);

        // Triage decides it was never a P1.
        incident.Priority = Priority.P3Moderate;
        await _service.OnPriorityChangedAsync(incident);

        var clock = incident.SlaInstances.Single();
        clock.SlaName.ShouldBe("P3 resolution");
        clock.State.ShouldBe(SlaState.InProgress);
        clock.BreachedAt.ShouldBeNull();
        incident.HasBreachedSla.ShouldBeFalse();
    }

    [Fact]
    public async Task A_settled_clock_is_not_re_targeted_by_a_later_priority_change()
    {
        var incident = Incident(Priority.P3Moderate);

        WithPolicies(
            Policy(Definition(SlaTargetType.Response, 15, "P1 response"), Priority.P1Critical, 5),
            Policy(Definition(SlaTargetType.Response, 120, "P3 response"), Priority.P3Moderate, 10));

        await _service.AttachClocksAsync(incident);
        incident.RecordFirstResponse(Now.AddMinutes(10));
        await _service.OnFirstResponseAsync(incident, incident.FirstRespondedAt!.Value);

        incident.Priority = Priority.P1Critical;
        await _service.OnPriorityChangedAsync(incident);

        // The response commitment was already met; escalating afterwards must not rewrite history.
        var clock = incident.SlaInstances.Single();
        clock.SlaName.ShouldBe("P3 response");
        clock.State.ShouldBe(SlaState.Met);
    }

    [Fact]
    public async Task The_next_due_roll_up_is_the_earliest_live_commitment()
    {
        var incident = Incident();
        WithPolicies(
            Policy(Definition(SlaTargetType.Response, 30, "P2 response"), Priority.P2High, 10),
            Policy(Definition(SlaTargetType.Resolution, 480, "P2 resolution"), Priority.P2High, 20));

        await _service.AttachClocksAsync(incident);

        incident.NextSlaDueAt.ShouldBe(Now.AddMinutes(30));

        incident.RecordFirstResponse(Now.AddMinutes(10));
        await _service.OnFirstResponseAsync(incident, incident.FirstRespondedAt!.Value);

        // Once the response clock settles, the resolution deadline becomes the one to watch.
        incident.NextSlaDueAt.ShouldBe(Now.AddMinutes(480));
    }

    [Fact]
    public async Task Describing_a_clock_reports_elapsed_remaining_and_consumption()
    {
        var incident = Incident();
        WithPolicies(Policy(Definition(SlaTargetType.Resolution, 240, "P2 resolution"), Priority.P2High, 10));

        await _service.AttachClocksAsync(incident);
        _clock.UtcNow.Returns(Now.AddMinutes(60));

        var snapshot = await _service.DescribeAsync(incident.SlaInstances.Single());

        snapshot.ElapsedMinutes.ShouldBe(60);
        snapshot.RemainingMinutes.ShouldBe(180);
        snapshot.ConsumedPercent.ShouldBe(25);
    }
}
