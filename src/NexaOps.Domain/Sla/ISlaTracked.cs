using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Sla;

/// <summary>
/// A record that carries SLA commitments.
/// <para>
/// The SLA engine works against this rather than against a specific aggregate, so incidents,
/// service requests and later modules share one implementation of the arithmetic. Everything the
/// engine needs to select a policy and stamp a clock is declared here; nothing else about the
/// record is visible to it.
/// </para>
/// <para>
/// This interface exists because the original engine was typed against <c>Incident</c> while its
/// storage was already polymorphic. The mismatch only surfaced when a second module needed
/// clocks, and it meant service requests could not have any.
/// </para>
/// </summary>
public interface ISlaTracked
{
    Guid Id { get; }

    Guid TenantId { get; }

    /// <summary>Human-facing identifier, used only in log messages.</summary>
    string Number { get; }

    /// <summary>Which module owns the record. Selects the policies that may apply.</summary>
    ServiceModule SlaModule { get; }

    Priority Priority { get; }

    Guid? CategoryId { get; }

    /// <summary>Null for modules with no subcategory concept.</summary>
    Guid? SlaSubcategoryId { get; }

    /// <summary>
    /// The owning group — the assignment group on an incident, the fulfilment group on a
    /// request. Named for the role it plays rather than for either module's own field.
    /// </summary>
    Guid? SlaGroupId { get; }

    Guid? OrganizationId { get; }

    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// The live clocks. Populated by the repository using <c>Module</c> and <c>RecordId</c>,
    /// never through an EF navigation - mapping one creates a foreign key that contradicts the
    /// table's polymorphism.
    /// </summary>
    ICollection<SlaInstance> SlaInstances { get; }

    /// <summary>Denormalised roll-ups, so list views can badge and sort without a join.</summary>
    bool HasBreachedSla { get; set; }

    DateTimeOffset? NextSlaDueAt { get; set; }
}

/// <summary>
/// What a status change means for the clocks, expressed in terms the SLA engine understands.
/// <para>
/// The engine deliberately does not know about <c>IncidentStatus</c> or <c>RequestStatus</c>.
/// Each module translates its own lifecycle into these flags, which keeps one set of rules and
/// lets the two modules differ where the business differs - a request pauses while it waits for
/// approval, an incident has no such state.
/// </para>
/// </summary>
/// <param name="WasPaused">Whether the previous status paused the clocks.</param>
/// <param name="IsPaused">Whether the new status pauses them.</param>
/// <param name="IsCancelled">The record was cancelled; live clocks are abandoned, not breached.</param>
/// <param name="IsCompleted">The work is done; resolution or fulfilment clocks are met.</param>
/// <param name="CompletedAt">When it was completed, so a clock is settled at the right moment.</param>
/// <param name="IsReopened">Completed work was reopened, so a settled clock resumes.</param>
public sealed record SlaStatusChange(
    bool WasPaused,
    bool IsPaused,
    bool IsCancelled = false,
    bool IsCompleted = false,
    DateTimeOffset? CompletedAt = null,
    bool IsReopened = false);

/// <summary>
/// Optional extra facts a record can supply so that clocks attached to already-progressed work
/// settle immediately, rather than starting a commitment that was met before it existed.
/// </summary>
public interface ISlaProgressFacts
{
    /// <summary>When first contact was made, or null where there is no response commitment.</summary>
    DateTimeOffset? FirstRespondedAt { get; }

    /// <summary>When the work was resolved or fulfilled, or null while it is outstanding.</summary>
    DateTimeOffset? SlaCompletedAt { get; }
}
