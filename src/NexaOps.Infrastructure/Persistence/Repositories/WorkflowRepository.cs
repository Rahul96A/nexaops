using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Common;
using NexaOps.Application.Workflows;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Workflows;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class WorkflowRepository : IWorkflowRepository
{
    private readonly NexaOpsDbContext _context;

    public WorkflowRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowDefinition>> GetRunnableAsync(
        ServiceModule module,
        WorkflowTrigger trigger,
        CancellationToken cancellationToken = default)
        // Tracked rather than no-tracking: the engine writes LastRunAt and RunCount back onto
        // the definitions it ran, and the run rows it adds hang off the same unit of work.
        => await _context.WorkflowDefinitions
            .Include(w => w.Conditions)
            .Include(w => w.Actions)
            .Where(w => w.Module == module && w.Trigger == trigger && w.IsActive)
            .OrderBy(w => w.Sequence)
            .ThenBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<WorkflowDefinition?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.WorkflowDefinitions
            .Include(w => w.Conditions)
            .Include(w => w.Actions)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(WorkflowDefinition definition) => _context.WorkflowDefinitions.Add(definition);

    /// <inheritdoc />
    public void Remove(WorkflowCondition condition) => _context.WorkflowConditions.Remove(condition);

    /// <inheritdoc />
    public void Remove(WorkflowAction action) => _context.WorkflowActions.Remove(action);

    /// <inheritdoc />
    public void AddRun(WorkflowRun run) => _context.WorkflowRuns.Add(run);

    /// <inheritdoc />
    public void SetExpectedVersion(WorkflowDefinition definition, byte[] rowVersion)
        => _context.Entry(definition).Property(w => w.RowVersion).OriginalValue = rowVersion;
}

/// <inheritdoc />
public sealed class WorkflowReferenceRepository : IWorkflowReferenceRepository
{
    private readonly NexaOpsDbContext _context;

    public WorkflowReferenceRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetGroupMemberIdsAsync(
        Guid groupId,
        CancellationToken cancellationToken = default)
        // Both sides are tenant-filtered by the global query filter, so a rule pointed at
        // another tenant's group returns nobody rather than somebody else's people.
        => await _context.GroupMembers
            .AsNoTracking()
            .Where(m => m.GroupId == groupId && m.User!.Status == UserStatus.Active)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> GroupExistsAsync(Guid groupId, CancellationToken cancellationToken = default)
        => await _context.Groups
            .AsNoTracking()
            .AnyAsync(g => g.Id == groupId && g.IsActive, cancellationToken)
            .ConfigureAwait(false);
}

/// <inheritdoc />
public sealed class WorkflowQueryService : IWorkflowQueryService
{
    private readonly NexaOpsDbContext _context;

    public WorkflowQueryService(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<PagedResult<WorkflowSummaryDto>> SearchAsync(
        WorkflowQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var source = _context.WorkflowDefinitions.AsNoTracking();

        if (query.Module is not null)
        {
            source = source.Where(w => w.Module == query.Module);
        }

        if (query.Trigger is not null)
        {
            source = source.Where(w => w.Trigger == query.Trigger);
        }

        if (query.IsActive is not null)
        {
            source = source.Where(w => w.IsActive == query.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(w => w.Name.Contains(term) || w.Description!.Contains(term));
        }

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<WorkflowSummaryDto>.Empty(query.Page, query.PageSize);
        }

        var items = await source
            // Active first, then in the order they will actually run: an administrator reading
            // the list is asking "what happens to an incident", and that is the answer.
            .OrderByDescending(w => w.IsActive)
            .ThenBy(w => w.Module)
            .ThenBy(w => w.Trigger)
            .ThenBy(w => w.Sequence)
            .Skip((Math.Max(query.Page, 1) - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(w => new WorkflowSummaryDto(
                w.Id,
                w.Name,
                w.Description,
                w.Module,
                w.Trigger,
                w.IsActive,
                w.Sequence,
                w.Conditions.Count,
                w.Actions.Count,
                w.LastRunAt,
                w.RunCount,
                w.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<WorkflowSummaryDto>(items, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<WorkflowDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var definition = await _context.WorkflowDefinitions
            .AsNoTracking()
            .Include(w => w.Conditions)
            .Include(w => w.Actions)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (definition is null)
        {
            return null;
        }

        // Names are resolved separately rather than through navigations, because the actions
        // deliberately have no foreign keys to users and groups: a rule pointing at somebody who
        // has left must still be readable, showing that the target is gone.
        var groupIds = definition.Actions.Where(a => a.TargetGroupId is not null)
            .Select(a => a.TargetGroupId!.Value).Distinct().ToList();

        var userIds = definition.Actions.Where(a => a.TargetUserId is not null)
            .Select(a => a.TargetUserId!.Value).Distinct().ToList();

        var groups = groupIds.Count == 0
            ? []
            : await _context.Groups.AsNoTracking()
                .Where(g => groupIds.Contains(g.Id))
                .ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken)
                .ConfigureAwait(false);

        var users = userIds.Count == 0
            ? []
            : await _context.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken)
                .ConfigureAwait(false);

        return new WorkflowDetailDto(
            definition.Id,
            definition.Name,
            definition.Description,
            definition.Module,
            definition.Trigger,
            definition.IsActive,
            definition.Sequence,
            [.. definition.Conditions.Select(c => new WorkflowConditionDto(c.Id, c.Field, c.Operator, c.Value))],
            [
                .. definition.Actions.OrderBy(a => a.Sequence).Select(a => new WorkflowActionDto(
                    a.Id,
                    a.Sequence,
                    a.Type,
                    a.Recipient,
                    a.TargetGroupId,
                    Lookup(groups, a.TargetGroupId),
                    a.TargetUserId,
                    Lookup(users, a.TargetUserId),
                    a.TargetPriority,
                    a.Message))
            ],
            definition.LastRunAt,
            definition.RunCount,
            definition.CreatedAt,
            definition.RowVersion);

        static string? Lookup(IReadOnlyDictionary<Guid, string> names, Guid? id)
            => id is not null && names.TryGetValue(id.Value, out var name) ? name : null;
    }

    /// <inheritdoc />
    public async Task<PagedResult<WorkflowRunDto>> GetRunsAsync(
        WorkflowRunQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var source = _context.WorkflowRuns.AsNoTracking();

        if (query.WorkflowDefinitionId is not null)
        {
            source = source.Where(r => r.WorkflowDefinitionId == query.WorkflowDefinitionId);
        }

        if (query.RecordId is not null)
        {
            source = source.Where(r => r.RecordId == query.RecordId);
        }

        if (query.Status is not null)
        {
            source = source.Where(r => r.Status == query.Status);
        }

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<WorkflowRunDto>.Empty(query.Page, query.PageSize);
        }

        var items = await source
            .OrderByDescending(r => r.StartedAt)
            .Skip((Math.Max(query.Page, 1) - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new WorkflowRunDto(
                r.Id,
                r.WorkflowDefinitionId,
                r.WorkflowName,
                r.Module,
                r.RecordId,
                r.RecordNumber,
                r.Trigger,
                r.Status,
                r.StartedAt,
                r.CompletedAt,
                r.Outcome,
                r.Steps
                    .OrderBy(s => s.Sequence)
                    .Select(s => new WorkflowStepRunDto(s.Sequence, s.ActionType, s.Status, s.Detail, s.Error))
                    .ToList()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<WorkflowRunDto>(items, total, query.Page, query.PageSize);
    }
}
