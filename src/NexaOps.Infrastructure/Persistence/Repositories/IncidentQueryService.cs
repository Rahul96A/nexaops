using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Security;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class IncidentQueryService : IIncidentQueryService
{
    private readonly NexaOpsDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ISlaRepositoryScheduleAccessor _schedules;

    public IncidentQueryService(
        NexaOpsDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ISlaRepositoryScheduleAccessor schedules)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
        _schedules = schedules;
    }

    /// <summary>
    /// The statuses that count as work still in flight. Shared by the scoped views and the
    /// dashboard counters so a figure and the queue behind it can never disagree.
    /// </summary>
    private static readonly IncidentStatus[] OpenStatuses =
    [
        IncidentStatus.New,
        IncidentStatus.Assigned,
        IncidentStatus.InProgress,
        IncidentStatus.Pending
    ];

    /// <summary>
    /// Restricts a query to the incidents this caller may see.
    /// <para>
    /// A caller with <c>incident.read.all</c> sees everything in their tenant. Everyone else sees
    /// only what they are involved in. This is applied as a database predicate rather than a
    /// post-filter so that counts, paging and aggregates are all correct, and so an invisible
    /// incident is indistinguishable from one that does not exist.
    /// </para>
    /// </summary>
    private IQueryable<Incident> VisibleIncidents()
    {
        var query = _context.Incidents.AsNoTracking();

        if (_currentUser.HasPermission(Permissions.IncidentReadAll))
        {
            return query;
        }

        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;
        var groupIds = _currentUser.GroupIds.ToList();

        return query.Where(i =>
            i.RequesterId == userId
            || i.AffectedUserId == userId
            || i.AssignedToUserId == userId
            || (i.AssignmentGroupId != null && groupIds.Contains(i.AssignmentGroupId.Value)));
    }

    /// <inheritdoc />
    public async Task<bool> CanViewAsync(Guid incidentId, CancellationToken cancellationToken = default)
        => await VisibleIncidents()
            .AnyAsync(i => i.Id == incidentId, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<PagedResult<IncidentListItemDto>> SearchAsync(
        IncidentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var source = ApplyFilters(VisibleIncidents(), query);

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
        {
            return PagedResult<IncidentListItemDto>.Empty(query.Page, query.PageSize);
        }

        var items = await ApplySort(source, query)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(i => new IncidentListItemDto(
                i.Id,
                i.Number,
                i.Title,
                i.Status,
                i.Priority,
                i.Impact,
                i.Urgency,
                i.CategoryId,
                i.Category != null ? i.Category.Name : null,
                i.Subcategory != null ? i.Subcategory.Name : null,
                i.RequesterId,
                i.Requester != null ? i.Requester.DisplayName : string.Empty,
                i.AssignedToUserId,
                i.AssignedTo != null ? i.AssignedTo.DisplayName : null,
                i.AssignmentGroupId,
                i.AssignmentGroup != null ? i.AssignmentGroup.Name : null,
                i.Channel,
                i.IsMajorIncident,
                i.HasBreachedSla,
                i.NextSlaDueAt,
                i.CreatedAt,
                i.UpdatedAt,
                i.ResolvedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<IncidentListItemDto>(items, total, query.Page, query.PageSize);
    }

    private IQueryable<Incident> ApplyFilters(IQueryable<Incident> source, IncidentQuery query)
    {
        if (!query.IncludeArchived)
        {
            source = source.Where(i => !i.IsArchived);
        }

        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;
        var groupIds = _currentUser.GroupIds.ToList();
        var now = _clock.UtcNow;

        source = query.Scope switch
        {
            IncidentViewScope.AssignedToMe =>
                source.Where(i => i.AssignedToUserId == userId
                                  && i.Status != IncidentStatus.Closed
                                  && i.Status != IncidentStatus.Cancelled),

            IncidentViewScope.MyTeam =>
                source.Where(i => i.AssignmentGroupId != null
                                  && groupIds.Contains(i.AssignmentGroupId.Value)
                                  && i.Status != IncidentStatus.Closed
                                  && i.Status != IncidentStatus.Cancelled),

            IncidentViewScope.RaisedByMe =>
                source.Where(i => i.RequesterId == userId || i.AffectedUserId == userId),

            // Unassigned, breached and due-soon are all "work someone must act on now", so they
            // are restricted to genuinely open statuses. A resolved incident that missed its
            // commitment is a reporting fact, not something an agent should be chased about, and
            // surfacing it under "needs attention" makes that panel untrustworthy.
            IncidentViewScope.Unassigned =>
                source.Where(i => i.AssignedToUserId == null && OpenStatuses.Contains(i.Status)),

            IncidentViewScope.Breached =>
                source.Where(i => i.HasBreachedSla && OpenStatuses.Contains(i.Status)),

            IncidentViewScope.DueSoon =>
                source.Where(i => i.NextSlaDueAt != null
                                  && i.NextSlaDueAt > now
                                  && i.NextSlaDueAt <= now.AddHours(4)
                                  && OpenStatuses.Contains(i.Status)),

            _ => source
        };

        if (query.OpenOnly == true)
        {
            source = source.Where(i => OpenStatuses.Contains(i.Status));
        }

        if (query.Statuses is { Count: > 0 })
        {
            var statuses = query.Statuses.ToList();
            source = source.Where(i => statuses.Contains(i.Status));
        }

        if (query.Priorities is { Count: > 0 })
        {
            var priorities = query.Priorities.ToList();
            source = source.Where(i => priorities.Contains(i.Priority));
        }

        if (query.AssignedToUserId is not null)
        {
            source = source.Where(i => i.AssignedToUserId == query.AssignedToUserId);
        }

        if (query.AssignmentGroupId is not null)
        {
            source = source.Where(i => i.AssignmentGroupId == query.AssignmentGroupId);
        }

        if (query.RequesterId is not null)
        {
            source = source.Where(i => i.RequesterId == query.RequesterId);
        }

        if (query.CategoryId is not null)
        {
            source = source.Where(i => i.CategoryId == query.CategoryId);
        }

        if (query.SubcategoryId is not null)
        {
            source = source.Where(i => i.SubcategoryId == query.SubcategoryId);
        }

        if (query.OrganizationId is not null)
        {
            source = source.Where(i => i.OrganizationId == query.OrganizationId);
        }

        if (query.DepartmentId is not null)
        {
            source = source.Where(i => i.DepartmentId == query.DepartmentId);
        }

        if (query.IsMajorIncident is not null)
        {
            source = source.Where(i => i.IsMajorIncident == query.IsMajorIncident);
        }

        if (query.HasBreachedSla is not null)
        {
            source = source.Where(i => i.HasBreachedSla == query.HasBreachedSla);
        }

        if (query.CreatedFrom is not null)
        {
            source = source.Where(i => i.CreatedAt >= query.CreatedFrom);
        }

        if (query.CreatedTo is not null)
        {
            source = source.Where(i => i.CreatedAt <= query.CreatedTo);
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim().ToLowerInvariant();
            source = source.Where(i => i.Tags.Any(t => t.Tag == tag));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Parameterised by EF Core; the term is never concatenated into SQL.
            var term = query.Search.Trim();
            source = source.Where(i =>
                i.Number.Contains(term)
                || i.Title.Contains(term)
                || i.Description.Contains(term));
        }

        return source;
    }

    /// <summary>
    /// Applies sorting from an allow-list. The client sends a field name, never an expression,
    /// and an unrecognised name falls back to a deterministic default rather than being
    /// interpolated anywhere near SQL.
    /// </summary>
    private static IQueryable<Incident> ApplySort(IQueryable<Incident> source, IncidentQuery query)
    {
        var ascending = query.SortDirection == SortDirection.Ascending;

        return (query.SortBy?.ToLowerInvariant()) switch
        {
            "number" => ascending ? source.OrderBy(i => i.Number) : source.OrderByDescending(i => i.Number),
            "title" => ascending ? source.OrderBy(i => i.Title) : source.OrderByDescending(i => i.Title),
            "status" => ascending ? source.OrderBy(i => i.Status) : source.OrderByDescending(i => i.Status),
            "priority" => ascending ? source.OrderBy(i => i.Priority) : source.OrderByDescending(i => i.Priority),
            "impact" => ascending ? source.OrderBy(i => i.Impact) : source.OrderByDescending(i => i.Impact),
            "urgency" => ascending ? source.OrderBy(i => i.Urgency) : source.OrderByDescending(i => i.Urgency),
            "updatedat" => ascending ? source.OrderBy(i => i.UpdatedAt) : source.OrderByDescending(i => i.UpdatedAt),
            "resolvedat" => ascending ? source.OrderBy(i => i.ResolvedAt) : source.OrderByDescending(i => i.ResolvedAt),
            "nextsladueat" => ascending
                ? source.OrderBy(i => i.NextSlaDueAt == null).ThenBy(i => i.NextSlaDueAt)
                : source.OrderBy(i => i.NextSlaDueAt == null).ThenByDescending(i => i.NextSlaDueAt),
            "requestername" => ascending
                ? source.OrderBy(i => i.Requester!.DisplayName)
                : source.OrderByDescending(i => i.Requester!.DisplayName),
            "assignedtoname" => ascending
                ? source.OrderBy(i => i.AssignedTo!.DisplayName)
                : source.OrderByDescending(i => i.AssignedTo!.DisplayName),
            _ => ascending ? source.OrderBy(i => i.CreatedAt) : source.OrderByDescending(i => i.CreatedAt)
        };
    }

    /// <inheritdoc />
    public async Task<IncidentDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var incident = await VisibleIncidents()
            .Include(i => i.Requester)
            .Include(i => i.AffectedUser)
            .Include(i => i.AssignedTo)
            .Include(i => i.AssignmentGroup)
            .Include(i => i.Category)
            .Include(i => i.Subcategory)
            .Include(i => i.ParentIncident)
            .Include(i => i.Tags)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (incident is null)
        {
            return null;
        }

        var clocks = await _context.SlaInstances
            .AsNoTracking()
            .Include(s => s.SlaDefinition)
            .Where(s => s.Module == ServiceModule.Incident && s.RecordId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var slaDtos = new List<SlaInstanceDto>(clocks.Count);
        var now = _clock.UtcNow;

        foreach (var clock in clocks)
        {
            var schedule = await _schedules
                .GetScheduleAsync(clock.BusinessCalendarId, cancellationToken)
                .ConfigureAwait(false);

            slaDtos.Add(new SlaInstanceDto(
                clock.Id,
                clock.SlaName,
                clock.TargetType,
                clock.State,
                clock.StartedAt,
                clock.DueAt,
                clock.CompletedAt,
                clock.BreachedAt,
                clock.DurationMinutes,
                clock.ElapsedBusinessMinutes(schedule, now),
                clock.RemainingBusinessMinutes(schedule, now),
                clock.ConsumedPercent(schedule, now),
                clock.WarningThresholdPercent));
        }

        var canSeeWorkNotes = _currentUser.HasPermission(Permissions.IncidentWorkNoteRead);

        var commentCount = await _context.IncidentComments
            .AsNoTracking()
            .CountAsync(
                c => c.IncidentId == id
                     && (canSeeWorkNotes || c.Kind == IncidentCommentKind.PublicComment),
                cancellationToken)
            .ConfigureAwait(false);

        var attachmentCount = await _context.Attachments
            .AsNoTracking()
            .CountAsync(
                a => a.Module == ServiceModule.Incident && a.RecordId == id && !a.IsArchived,
                cancellationToken)
            .ConfigureAwait(false);

        var names = await ResolveUserNamesAsync(
            [incident.CreatedBy, incident.UpdatedBy, incident.ResolvedByUserId],
            cancellationToken).ConfigureAwait(false);

        var organizationName = incident.OrganizationId is null
            ? null
            : await _context.Organizations.AsNoTracking()
                .Where(o => o.Id == incident.OrganizationId)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var departmentName = incident.DepartmentId is null
            ? null
            : await _context.Departments.AsNoTracking()
                .Where(d => d.Id == incident.DepartmentId)
                .Select(d => d.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return new IncidentDetailDto
        {
            Id = incident.Id,
            Number = incident.Number,
            Title = incident.Title,
            Description = incident.Description,
            Status = incident.Status,
            PendingReason = incident.PendingReason,
            Impact = incident.Impact,
            Urgency = incident.Urgency,
            Priority = incident.Priority,
            IsPriorityOverridden = incident.IsPriorityOverridden,
            PriorityOverrideReason = incident.PriorityOverrideReason,
            Channel = incident.Channel,
            IsMajorIncident = incident.IsMajorIncident,
            RequesterId = incident.RequesterId,
            RequesterName = incident.Requester?.DisplayName ?? string.Empty,
            RequesterEmail = incident.Requester?.Email,
            AffectedUserId = incident.AffectedUserId,
            AffectedUserName = incident.AffectedUser?.DisplayName,
            OrganizationId = incident.OrganizationId,
            OrganizationName = organizationName,
            DepartmentId = incident.DepartmentId,
            DepartmentName = departmentName,
            CategoryId = incident.CategoryId,
            CategoryName = incident.Category?.Name,
            SubcategoryId = incident.SubcategoryId,
            SubcategoryName = incident.Subcategory?.Name,
            AssignmentGroupId = incident.AssignmentGroupId,
            AssignmentGroupName = incident.AssignmentGroup?.Name,
            AssignedToUserId = incident.AssignedToUserId,
            AssignedToName = incident.AssignedTo?.DisplayName,
            ResolutionCode = incident.ResolutionCode,
            ResolutionNotes = incident.ResolutionNotes,
            FirstRespondedAt = incident.FirstRespondedAt,
            ResolvedAt = incident.ResolvedAt,
            ResolvedByName = Lookup(names, incident.ResolvedByUserId),
            ClosedAt = incident.ClosedAt,
            ReopenCount = incident.ReopenCount,
            ParentIncidentId = incident.ParentIncidentId,
            ParentIncidentNumber = incident.ParentIncident?.Number,
            CreatedAt = incident.CreatedAt,
            CreatedByName = Lookup(names, incident.CreatedBy),
            UpdatedAt = incident.UpdatedAt,
            UpdatedByName = Lookup(names, incident.UpdatedBy),
            Tags = incident.Tags.Select(t => t.Tag).OrderBy(t => t, StringComparer.Ordinal).ToList(),
            SlaInstances = slaDtos,
            AllowedTransitions = IncidentStateMachine.AllowedTransitionsFrom(incident.Status).ToList(),
            AttachmentCount = attachmentCount,
            CommentCount = commentCount,
            ConcurrencyToken = incident.RowVersion is null ? null : Convert.ToBase64String(incident.RowVersion)
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IncidentCommentDto>> GetCommentsAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        var canSeeWorkNotes = _currentUser.HasPermission(Permissions.IncidentWorkNoteRead);

        return await _context.IncidentComments
            .AsNoTracking()
            .Include(c => c.Author)
            .Where(c => c.IncidentId == incidentId
                        && (canSeeWorkNotes || c.Kind == IncidentCommentKind.PublicComment))
            .OrderBy(c => c.CreatedAt)
            .Select(c => new IncidentCommentDto(
                c.Id,
                c.Kind,
                c.Body,
                c.AuthorUserId,
                c.Author != null ? c.Author.DisplayName : "Unknown",
                c.Author != null ? c.Author.AvatarColor : null,
                c.IsSystemGenerated,
                c.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IncidentActivityDto>> GetActivityAsync(
        Guid incidentId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 500);
        var comments = await GetCommentsAsync(incidentId, cancellationToken).ConfigureAwait(false);

        var activity = comments
            .Select(c => new IncidentActivityDto(
                c.Id,
                c.Kind == IncidentCommentKind.WorkNote ? "work_note" : "comment",
                c.CreatedAt,
                c.AuthorUserId,
                c.AuthorName,
                c.AuthorAvatarColor,
                c.Body,
                c.Kind,
                c.IsSystemGenerated,
                null))
            .ToList();

        // Field changes come from the audit trail, so the timeline and the audit tab can never
        // disagree. Audit reading is permission-gated; without it the timeline shows comments
        // only rather than failing.
        if (_currentUser.HasPermission(Permissions.AuditRead))
        {
            var id = incidentId.ToString();

            var audits = await _context.AuditEvents
                .AsNoTracking()
                .Where(a => a.EntityType == nameof(Incident)
                            && a.EntityId == id
                            && a.ChangedFields != null)
                .OrderByDescending(a => a.OccurredAt)
                .Take(take)
                .Select(a => new
                {
                    a.Id,
                    a.OccurredAt,
                    a.ActorUserId,
                    a.ActorDisplayName,
                    a.ChangedFields,
                    a.BeforeJson,
                    a.AfterJson
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            activity.AddRange(audits.Select(a => new IncidentActivityDto(
                a.Id,
                "field_change",
                a.OccurredAt,
                a.ActorUserId,
                a.ActorDisplayName,
                null,
                null,
                null,
                false,
                BuildChanges(a.ChangedFields, a.BeforeJson, a.AfterJson))));
        }

        return activity
            .OrderByDescending(a => a.OccurredAt)
            .Take(take)
            .ToList();
    }

    /// <summary>Turns the stored JSON snapshots into per-field before/after pairs for the timeline.</summary>
    private static IReadOnlyList<IncidentFieldChangeDto> BuildChanges(
        string? changedFields,
        string? beforeJson,
        string? afterJson)
    {
        if (string.IsNullOrWhiteSpace(changedFields))
        {
            return [];
        }

        var before = ParseJsonObject(beforeJson);
        var after = ParseJsonObject(afterJson);

        return changedFields
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(field => new IncidentFieldChangeDto(
                field,
                before.GetValueOrDefault(field),
                after.GetValueOrDefault(field)))
            .ToList();
    }

    private static Dictionary<string, string?> ParseJsonObject(string? json)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind == System.Text.Json.JsonValueKind.Null
                    ? null
                    : property.Value.ToString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // A malformed snapshot must not break the timeline; show the field names only.
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<ServiceDeskSummaryDto> GetServiceDeskSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;
        var groupIds = _currentUser.GroupIds.ToList();
        var todayStart = now.Date;

        var visible = VisibleIncidents().Where(i => !i.IsArchived);

        var open = visible.Where(i => OpenStatuses.Contains(i.Status));

        // One aggregate query rather than ten round trips.
        var counts = await open
            .GroupBy(i => 1)
            .Select(g => new
            {
                OpenIncidents = g.Count(),
                CriticalOpen = g.Count(i => i.Priority == Priority.P1Critical),
                HighOpen = g.Count(i => i.Priority == Priority.P2High),
                UnassignedInMyGroups = g.Count(i =>
                    i.AssignedToUserId == null
                    && i.AssignmentGroupId != null
                    && groupIds.Contains(i.AssignmentGroupId.Value)),
                AssignedToMe = g.Count(i => i.AssignedToUserId == userId),
                RaisedByMe = g.Count(i => i.RequesterId == userId),
                BreachedOpen = g.Count(i => i.HasBreachedSla),
                DueWithinTwoHours = g.Count(i =>
                    i.NextSlaDueAt != null
                    && i.NextSlaDueAt > now
                    && i.NextSlaDueAt <= now.AddHours(2))
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var createdToday = await visible
            .CountAsync(i => i.CreatedAt >= todayStart, cancellationToken)
            .ConfigureAwait(false);

        var resolvedToday = await visible
            .CountAsync(i => i.ResolvedAt != null && i.ResolvedAt >= todayStart, cancellationToken)
            .ConfigureAwait(false);

        var byPriority = await open
            .GroupBy(i => i.Priority)
            .Select(g => new PriorityCountDto(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byStatus = await open
            .GroupBy(i => i.Status)
            .Select(g => new StatusCountDto(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Workload covers the caller's own groups; with no group membership it covers whatever
        // they can see, so a manager without an explicit group still gets a useful board.
        var workloadSource = groupIds.Count > 0
            ? open.Where(i => i.AssignmentGroupId != null && groupIds.Contains(i.AssignmentGroupId.Value))
            : open;

        // Grouped on the key alone. Grouping by an anonymous key that reaches through a
        // navigation for the agent's name does not translate to SQL, so the names are joined
        // back on afterwards rather than pulled into the GROUP BY.
        var workloadCounts = await workloadSource
            .Where(i => i.AssignedToUserId != null)
            .GroupBy(i => i.AssignedToUserId!.Value)
            .Select(g => new
            {
                UserId = g.Key,
                OpenCount = g.Count(),
                BreachedCount = g.Count(i => i.HasBreachedSla)
            })
            .OrderByDescending(w => w.OpenCount)
            .Take(12)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var workloadUserIds = workloadCounts.Select(w => w.UserId).ToList();

        var agentNames = await _context.Users
            .AsNoTracking()
            .Where(u => workloadUserIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.AvatarColor })
            .ToDictionaryAsync(u => u.Id, cancellationToken)
            .ConfigureAwait(false);

        var workload = workloadCounts
            .Select(w => new AgentWorkloadDto(
                w.UserId,
                agentNames.TryGetValue(w.UserId, out var agent) ? agent.DisplayName : "Unknown",
                agent?.AvatarColor,
                w.OpenCount,
                w.BreachedCount))
            .ToList();

        return new ServiceDeskSummaryDto(
            counts?.OpenIncidents ?? 0,
            counts?.CriticalOpen ?? 0,
            counts?.HighOpen ?? 0,
            counts?.UnassignedInMyGroups ?? 0,
            counts?.AssignedToMe ?? 0,
            counts?.RaisedByMe ?? 0,
            counts?.BreachedOpen ?? 0,
            counts?.DueWithinTwoHours ?? 0,
            resolvedToday,
            createdToday,
            workload,
            byPriority.OrderBy(p => p.Priority).ToList(),
            byStatus.OrderBy(s => s.Status).ToList());
    }

    private async Task<Dictionary<Guid, string>> ResolveUserNamesAsync(
        IEnumerable<Guid?> ids,
        CancellationToken cancellationToken)
    {
        var distinct = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return [];
        }

        return await _context.Users
            .AsNoTracking()
            .Where(u => distinct.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? Lookup(Dictionary<Guid, string> names, Guid? id)
        => id is not null && names.TryGetValue(id.Value, out var name) ? name : null;
}

/// <summary>
/// The slice of SLA configuration the read path needs. Kept as its own interface so the query
/// service does not take a dependency on the whole write-side SLA repository.
/// </summary>
public interface ISlaRepositoryScheduleAccessor
{
    Task<BusinessSchedule> GetScheduleAsync(
        Guid? businessCalendarId,
        CancellationToken cancellationToken = default);
}
