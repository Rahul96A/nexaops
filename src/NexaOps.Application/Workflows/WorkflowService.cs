using FluentValidation;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Security;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Workflows;

namespace NexaOps.Application.Workflows;

/// <summary>Administration of automation rules, and the history of what they did.</summary>
public interface IWorkflowService
{
    Task<PagedResult<WorkflowSummaryDto>> SearchAsync(WorkflowQuery query, CancellationToken ct = default);

    Task<WorkflowDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<WorkflowRunDto>> GetRunsAsync(WorkflowRunQuery query, CancellationToken ct = default);

    /// <summary>The fields a rule may be written against, for the module named.</summary>
    IReadOnlyList<WorkflowFieldDto> GetAvailableFields(ServiceModule module);

    Task<WorkflowDetailDto> CreateAsync(UpsertWorkflowCommand command, CancellationToken ct = default);

    Task<WorkflowDetailDto> UpdateAsync(Guid id, UpsertWorkflowCommand command, CancellationToken ct = default);

    /// <summary>Switches a rule on or off. Rules are never deleted — see the definition.</summary>
    Task<WorkflowDetailDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class WorkflowService : IWorkflowService
{
    private const string EntityType = nameof(WorkflowDefinition);

    private readonly IWorkflowRepository _workflows;
    private readonly IWorkflowQueryService _queries;
    private readonly IWorkflowReferenceRepository _reference;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        IWorkflowRepository workflows,
        IWorkflowQueryService queries,
        IWorkflowReferenceRepository reference,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ILogger<WorkflowService> logger)
    {
        _workflows = workflows;
        _queries = queries;
        _reference = reference;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<PagedResult<WorkflowSummaryDto>> SearchAsync(WorkflowQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _currentUser.DemandPermission(Permissions.WorkflowRead);

        return _queries.SearchAsync(query, ct);
    }

    /// <inheritdoc />
    public async Task<WorkflowDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.WorkflowRead);

        return await _queries.GetAsync(id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, id);
    }

    /// <inheritdoc />
    public Task<PagedResult<WorkflowRunDto>> GetRunsAsync(WorkflowRunQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _currentUser.DemandPermission(Permissions.WorkflowRead);

        return _queries.GetRunsAsync(query, ct);
    }

    /// <inheritdoc />
    public IReadOnlyList<WorkflowFieldDto> GetAvailableFields(ServiceModule module)
    {
        _currentUser.DemandPermission(Permissions.WorkflowRead);

        return WorkflowFields.For(module);
    }

    /// <inheritdoc />
    public async Task<WorkflowDetailDto> CreateAsync(UpsertWorkflowCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.WorkflowManage);

        await ValidateAsync(command, ct).ConfigureAwait(false);

        var definition = new WorkflowDefinition
        {
            Name = command.Name.Trim(),
            Description = Clean(command.Description),
            Module = command.Module,
            Trigger = command.Trigger,
            IsActive = command.IsActive,
            Sequence = command.Sequence
        };

        Apply(command, definition);

        _workflows.Add(definition);

        _audit.Record(
            AuditAction.Create,
            EntityType,
            definition.Id.ToString(),
            definition.Name,
            $"Created automation rule \"{definition.Name}\" on {command.Module}/{command.Trigger}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Workflow {Workflow} created.", definition.Id);

        return await GetAsync(definition.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WorkflowDetailDto> UpdateAsync(
        Guid id,
        UpsertWorkflowCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.WorkflowManage);

        var definition = await LoadAsync(id, ct).ConfigureAwait(false);

        if (command.RowVersion is { Length: > 0 })
        {
            _workflows.SetExpectedVersion(definition, command.RowVersion);
        }

        await ValidateAsync(command, ct).ConfigureAwait(false);

        // The module and trigger are fixed once a rule exists. Changing them would leave the
        // run history describing a rule that no longer means what those runs meant, and the
        // conditions would silently be testing fields the new module does not publish.
        if (definition.Module != command.Module || definition.Trigger != command.Trigger)
        {
            throw new DomainException(
                "workflow.trigger_immutable",
                "A rule's module and trigger cannot be changed. Deactivate this rule and create another.");
        }

        definition.Name = command.Name.Trim();
        definition.Description = Clean(command.Description);
        definition.IsActive = command.IsActive;
        definition.Sequence = command.Sequence;

        // Conditions and actions are replaced wholesale rather than merged. They have no
        // independent identity to a user — nobody refers to "condition 2" — and a merge would
        // need stable client-supplied identifiers for no benefit.
        foreach (var condition in definition.Conditions.ToList())
        {
            _workflows.Remove(condition);
        }

        foreach (var action in definition.Actions.ToList())
        {
            _workflows.Remove(action);
        }

        definition.Conditions.Clear();
        definition.Actions.Clear();

        Apply(command, definition);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            definition.Id.ToString(),
            definition.Name,
            $"Updated automation rule \"{definition.Name}\".");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WorkflowDetailDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.WorkflowManage);

        var definition = await LoadAsync(id, ct).ConfigureAwait(false);

        if (isActive && definition.Actions.Count == 0)
        {
            throw new DomainException(
                "workflow.no_actions",
                "A rule with no actions cannot be activated: it would record a run against every "
                + "matching record and do nothing.");
        }

        definition.IsActive = isActive;

        _audit.Record(
            AuditAction.Configuration,
            EntityType,
            definition.Id.ToString(),
            definition.Name,
            $"Automation rule \"{definition.Name}\" {(isActive ? "activated" : "deactivated")}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    private async Task<WorkflowDefinition> LoadAsync(Guid id, CancellationToken ct)
        => await _workflows.GetAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private static void Apply(UpsertWorkflowCommand command, WorkflowDefinition definition)
    {
        foreach (var condition in command.Conditions)
        {
            definition.Conditions.Add(new WorkflowCondition
            {
                Field = condition.Field.Trim(),
                Operator = condition.Operator,
                Value = Clean(condition.Value)
            });
        }

        var sequence = 0;

        foreach (var action in command.Actions.OrderBy(a => a.Sequence))
        {
            definition.Actions.Add(new WorkflowAction
            {
                // Renumbered from zero on save. Client-supplied sequences with gaps or
                // duplicates would make the execution order depend on how the form was edited.
                Sequence = sequence++,
                Type = action.Type,
                Recipient = action.Recipient,
                TargetGroupId = action.TargetGroupId,
                TargetUserId = action.TargetUserId,
                TargetPriority = action.TargetPriority,
                Message = Clean(action.Message)
            });
        }
    }

    /// <summary>
    /// Checks the rule is one that can actually run.
    /// <para>
    /// Validated here rather than at execution because a rule that fails every time it fires is
    /// a configuration mistake, and the moment to tell somebody about it is while they are
    /// looking at the form — not in a run history they will read next month.
    /// </para>
    /// </summary>
    private async Task ValidateAsync(UpsertWorkflowCommand command, CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            failures.Add(new(
                nameof(command.Name),
                "A rule needs a name. It is what appears in the run history."));
        }

        if (command.Actions.Count == 0 && command.IsActive)
        {
            failures.Add(new(nameof(command.Actions), "An active rule needs at least one action."));
        }

        var known = WorkflowFields.For(command.Module).Select(f => f.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var condition in command.Conditions)
        {
            if (!known.Contains(condition.Field?.Trim() ?? string.Empty))
            {
                // A rule written against a field the module does not publish would never match,
                // and would look configured. Better to refuse it than to store a rule that
                // silently does nothing.
                failures.Add(new(
                    nameof(command.Conditions),
                    $"\"{condition.Field}\" is not a field a {command.Module} rule can test. "
                    + "The available fields are listed alongside the module."));
                break;
            }

            var needsValue = condition.Operator is not (WorkflowConditionOperator.IsEmpty
                or WorkflowConditionOperator.IsNotEmpty);

            if (needsValue && string.IsNullOrWhiteSpace(condition.Value))
            {
                failures.Add(new(
                    nameof(command.Conditions),
                    $"The condition on \"{condition.Field}\" needs a value to compare against."));
                break;
            }
        }

        foreach (var action in command.Actions)
        {
            var message = await ValidateActionAsync(action, ct).ConfigureAwait(false);

            if (message is not null)
            {
                failures.Add(new(nameof(command.Actions), message));
                break;
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    private async Task<string?> ValidateActionAsync(UpsertWorkflowActionCommand action, CancellationToken ct)
    {
        switch (action.Type)
        {
            case WorkflowActionType.NotifyUser or WorkflowActionType.NotifyGroup:
                if (action.Recipient is null)
                {
                    return "A notification action needs somebody to notify.";
                }

                if (action.Recipient == WorkflowRecipient.SpecificUser && action.TargetUserId is null)
                {
                    return "Notifying a specific person needs that person to be named.";
                }

                if (action.Recipient == WorkflowRecipient.SpecificGroup && action.TargetGroupId is null)
                {
                    return "Notifying a specific group needs that group to be named.";
                }

                break;

            case WorkflowActionType.AssignToGroup when action.TargetGroupId is null:
                return "An assignment action needs a group.";

            case WorkflowActionType.AssignToUser when action.TargetUserId is null:
                return "An assignment action needs a person.";

            case WorkflowActionType.SetPriority when action.TargetPriority is null:
                return "A priority action needs a priority.";

            case WorkflowActionType.RequestApproval
                when action.TargetUserId is null && action.TargetGroupId is null:
                return "An approval action needs an approver: a person or a group.";
        }

        // Targets are checked against the tenant's own directory, so a rule cannot be pointed at
        // a neighbouring tenant's group or user — those read as non-existent here.
        if (action.TargetGroupId is not null
            && !await _reference.GroupExistsAsync(action.TargetGroupId.Value, ct).ConfigureAwait(false))
        {
            return "That group does not exist.";
        }

        if (action.TargetUserId is not null
            && !await _reference.UserExistsAsync(action.TargetUserId.Value, ct).ConfigureAwait(false))
        {
            return "That person does not exist or is no longer active.";
        }

        return null;
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
