using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Security;
using NexaOps.Domain.Auditing;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class AuditService : IAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NexaOpsDbContext _context;
    private readonly IDbContextFactory<NexaOpsDbContext> _contextFactory;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly ICorrelationContext _correlation;
    private readonly IDateTimeProvider _clock;

    public AuditService(
        NexaOpsDbContext context,
        IDbContextFactory<NexaOpsDbContext> contextFactory,
        ITenantContext tenant,
        ICurrentUser currentUser,
        ICorrelationContext correlation,
        IDateTimeProvider clock)
    {
        _context = context;
        _contextFactory = contextFactory;
        _tenant = tenant;
        _currentUser = currentUser;
        _correlation = correlation;
        _clock = clock;
    }

    /// <inheritdoc />
    public void Record(
        AuditAction action,
        string entityType,
        string? entityId = null,
        string? entityLabel = null,
        string? message = null,
        AuditSource source = AuditSource.Api,
        AuditOutcome outcome = AuditOutcome.Success,
        object? before = null,
        object? after = null)
        => _context.AuditEvents.Add(Build(
            action, entityType, entityId, entityLabel, message, source, outcome,
            before, after, tenantIdOverride: null, actorUserIdOverride: null));

    /// <inheritdoc />
    public async Task RecordImmediateAsync(
        AuditAction action,
        string entityType,
        string? entityId = null,
        string? entityLabel = null,
        string? message = null,
        AuditSource source = AuditSource.Api,
        AuditOutcome outcome = AuditOutcome.Success,
        Guid? tenantIdOverride = null,
        Guid? actorUserIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        // A separate context so the row survives even when the caller's transaction rolls back.
        // A denied permission or a failed sign-in must be recorded precisely because the
        // operation that triggered it did not complete.
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        context.AuditEvents.Add(Build(
            action, entityType, entityId, entityLabel, message, source, outcome,
            before: null, after: null, tenantIdOverride, actorUserIdOverride));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private AuditEvent Build(
        AuditAction action,
        string entityType,
        string? entityId,
        string? entityLabel,
        string? message,
        AuditSource source,
        AuditOutcome outcome,
        object? before,
        object? after,
        Guid? tenantIdOverride,
        Guid? actorUserIdOverride)
        => new()
        {
            TenantId = tenantIdOverride ?? (_tenant.HasTenant ? _tenant.TenantId : Guid.Empty),
            OccurredAt = _clock.UtcNow,
            ActorUserId = actorUserIdOverride ?? _currentUser.UserIdOrNull,
            ActorDisplayName = _currentUser.IsAuthenticated ? _currentUser.DisplayName : "System",
            ActorEmail = _currentUser.IsAuthenticated ? _currentUser.Email : null,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            EntityLabel = Truncate(entityLabel, 256),
            Message = Truncate(message, 2048),
            Source = source,
            Outcome = outcome,
            BeforeJson = before is null ? null : Truncate(JsonSerializer.Serialize(before, JsonOptions), 32000),
            AfterJson = after is null ? null : Truncate(JsonSerializer.Serialize(after, JsonOptions), 32000),
            CorrelationId = _correlation.CorrelationId,
            IpAddress = _correlation.IpAddress,
            UserAgent = Truncate(_correlation.UserAgent, 512)
        };

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}

/// <inheritdoc />
public sealed class AuditQueryService : IAuditQueryService
{
    private readonly NexaOpsDbContext _context;
    private readonly ICurrentUser _currentUser;

    public AuditQueryService(NexaOpsDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<PagedResult<AuditEventDto>> SearchAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _currentUser.DemandPermission(Permissions.AuditRead);

        var source = _context.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            source = source.Where(a => a.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            source = source.Where(a => a.EntityId == query.EntityId);
        }

        if (query.ActorUserId is not null)
        {
            source = source.Where(a => a.ActorUserId == query.ActorUserId);
        }

        if (query.Action is not null)
        {
            source = source.Where(a => a.Action == query.Action);
        }

        if (query.Source is not null)
        {
            source = source.Where(a => a.Source == query.Source);
        }

        if (query.Outcome is not null)
        {
            source = source.Where(a => a.Outcome == query.Outcome);
        }

        if (query.From is not null)
        {
            source = source.Where(a => a.OccurredAt >= query.From);
        }

        if (query.To is not null)
        {
            source = source.Where(a => a.OccurredAt <= query.To);
        }

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
        {
            source = source.Where(a => a.CorrelationId == query.CorrelationId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(a =>
                (a.EntityLabel != null && a.EntityLabel.Contains(term))
                || (a.Message != null && a.Message.Contains(term))
                || (a.ActorDisplayName != null && a.ActorDisplayName.Contains(term)));
        }

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await source
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(a => Project(a))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<AuditEventDto>(items, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditEventDto>> GetForRecordAsync(
        string entityType,
        Guid entityId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        _currentUser.DemandPermission(Permissions.AuditRead);

        var id = entityId.ToString();

        return await _context.AuditEvents
            .AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == id)
            .OrderByDescending(a => a.OccurredAt)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(a => Project(a))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Shared projection so list and record views cannot drift apart.</summary>
    private static AuditEventDto Project(AuditEvent a) => new(
        a.Id,
        a.OccurredAt,
        a.ActorUserId,
        a.ActorDisplayName,
        a.Action.ToString(),
        a.EntityType,
        a.EntityId,
        a.EntityLabel,
        a.Source.ToString(),
        a.Outcome.ToString(),
        a.ChangedFields,
        a.BeforeJson,
        a.AfterJson,
        a.Message,
        a.CorrelationId,
        a.IpAddress);
}
