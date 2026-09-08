using NexaOps.Application.Common;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Workflows;

namespace NexaOps.Application.Workflows;

/// <summary>Persistence for automation rules and their run history.</summary>
public interface IWorkflowRepository
{
    /// <summary>
    /// The active, runnable rules for one module and trigger, in evaluation order, with their
    /// conditions and actions loaded.
    /// <para>
    /// Loaded in one query rather than lazily: this runs inside the request that just changed a
    /// record, and a lazy load per rule would turn a five-rule tenant into a dozen round trips
    /// on the hot path of every incident update.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<WorkflowDefinition>> GetRunnableAsync(
        ServiceModule module,
        WorkflowTrigger trigger,
        CancellationToken cancellationToken = default);

    Task<WorkflowDefinition?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(WorkflowDefinition definition);

    void Remove(WorkflowCondition condition);

    void Remove(WorkflowAction action);

    void AddRun(WorkflowRun run);

    void SetExpectedVersion(WorkflowDefinition definition, byte[] rowVersion);
}

/// <summary>Read models for the administration screens. No tracking, projected in SQL.</summary>
public interface IWorkflowQueryService
{
    Task<PagedResult<WorkflowSummaryDto>> SearchAsync(
        WorkflowQuery query,
        CancellationToken cancellationToken = default);

    Task<WorkflowDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<WorkflowRunDto>> GetRunsAsync(
        WorkflowRunQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Look-ups the engine needs while executing actions.
/// <para>
/// Separate from the service desk reference repository because the engine asks a question none
/// of the modules do: who is in this group, in order to notify all of them.
/// </para>
/// </summary>
public interface IWorkflowReferenceRepository
{
    /// <summary>
    /// Every active member of a group. Tenant-filtered, so a rule cannot be pointed at a
    /// neighbouring tenant's group — it reads as empty rather than as somebody else's people.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetGroupMemberIdsAsync(
        Guid groupId,
        CancellationToken cancellationToken = default);

    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> GroupExistsAsync(Guid groupId, CancellationToken cancellationToken = default);
}
