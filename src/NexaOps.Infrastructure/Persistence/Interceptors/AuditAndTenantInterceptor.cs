using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;

namespace NexaOps.Infrastructure.Persistence.Interceptors;

/// <summary>
/// The single choke point every write passes through. It does three jobs, in this order:
/// <list type="number">
/// <item>stamps created/updated provenance,</item>
/// <item>enforces tenant isolation on writes,</item>
/// <item>captures before/after snapshots into the audit trail.</item>
/// </list>
/// <para>
/// Putting all three here rather than in application services means a new module, a background
/// job, or a future AI action gets them automatically. There is no "remember to audit this"
/// convention to forget.
/// </para>
/// </summary>
public sealed class AuditAndTenantInterceptor : SaveChangesInterceptor
{
    /// <summary>
    /// Columns that must never appear in an audit snapshot. A password hash or a token hash in
    /// the audit trail would turn a read-only audit permission into a credential disclosure.
    /// </summary>
    private static readonly HashSet<string> RedactedProperties = new(StringComparer.Ordinal)
    {
        "PasswordHash",
        "SecurityStamp",
        "TokenHash",
        "ReplacedByTokenHash"
    };

    /// <summary>Properties whose changes carry no information worth auditing.</summary>
    private static readonly HashSet<string> IgnoredProperties = new(StringComparer.Ordinal)
    {
        nameof(IAuditable.UpdatedAt),
        nameof(IAuditable.UpdatedBy),
        nameof(IConcurrencyAware.RowVersion)
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly ICorrelationContext _correlation;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AuditAndTenantInterceptor> _logger;

    public AuditAndTenantInterceptor(
        ITenantContext tenant,
        ICurrentUser currentUser,
        ICorrelationContext correlation,
        IDateTimeProvider clock,
        ILogger<AuditAndTenantInterceptor> logger)
    {
        _tenant = tenant;
        _currentUser = currentUser;
        _correlation = correlation;
        _clock = clock;
        _logger = logger;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is not null)
        {
            Process(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is not null)
        {
            Process(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void Process(DbContext context)
    {
        var now = _clock.UtcNow;
        var actorId = _currentUser.UserIdOrNull;
        var auditRows = new List<AuditEvent>();

        // Provenance stamping and the tenant guard always run. Only snapshot capture can be
        // suppressed, and only for bulk provisioning.
        var captureAudit = context is not NexaOpsDbContext { SuppressAudit: true };

        // Materialise first: adding audit rows below mutates the change tracker.
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            // Audit rows are themselves append-only and are never audited - that would recurse.
            if (entry.Entity is AuditEvent)
            {
                continue;
            }

            StampProvenance(entry, now, actorId);
            EnforceTenantIsolation(entry);

            if (!captureAudit)
            {
                continue;
            }

            var auditEvent = BuildAuditEvent(entry, now, actorId);
            if (auditEvent is not null)
            {
                auditRows.Add(auditEvent);
            }
        }

        foreach (var row in auditRows)
        {
            context.Add(row);
        }
    }

    /// <summary>Fills in created/updated timestamps and actor ids, and stamps the tenant on insert.</summary>
    private void StampProvenance(EntityEntry entry, DateTimeOffset now, Guid? actorId)
    {
        if (entry.Entity is IAuditable auditable)
        {
            if (entry.State == EntityState.Added)
            {
                if (auditable.CreatedAt == default)
                {
                    auditable.CreatedAt = now;
                }

                auditable.CreatedBy ??= actorId;
            }
            else if (entry.State == EntityState.Modified)
            {
                auditable.UpdatedAt = now;
                auditable.UpdatedBy = actorId;

                // CreatedAt/CreatedBy are write-once. Reverting them defeats an attempt to
                // rewrite provenance through a crafted payload bound onto a tracked entity.
                entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
            }
        }

        if (entry.Entity is ITenantOwned owned
            && entry.State == EntityState.Added
            && owned.TenantId == Guid.Empty
            && _tenant.HasTenant)
        {
            owned.TenantId = _tenant.TenantId;
        }
    }

    /// <summary>
    /// Refuses any write whose tenant does not match the ambient tenant.
    /// <para>
    /// This catches three distinct failures: an insert carrying a foreign tenant id, an update to
    /// a row loaded outside a tenant scope, and an attempt to move a row between tenants by
    /// editing <c>TenantId</c>. The last is checked against the <em>original</em> value, because
    /// comparing only the current value would let a rewrite that sets the correct-looking tenant
    /// slip through.
    /// </para>
    /// </summary>
    private void EnforceTenantIsolation(EntityEntry entry)
    {
        if (entry.Entity is not ITenantOwned owned)
        {
            return;
        }

        // Cross-tenant infrastructure work (the SLA monitor, platform administration) opts out
        // explicitly and is audited at its own boundary.
        if (entry.Context is NexaOpsDbContext { IgnoreTenantFilter: true })
        {
            return;
        }

        if (!_tenant.HasTenant)
        {
            throw new TenantIsolationViolationException(
                entry.Entity.GetType().Name,
                Guid.Empty,
                owned.TenantId);
        }

        var expected = _tenant.TenantId;

        if (owned.TenantId != expected)
        {
            _logger.LogCritical(
                "Tenant isolation violation: refused to write {EntityType} belonging to tenant {ActualTenant} while scoped to {ExpectedTenant}. Correlation {CorrelationId}.",
                entry.Entity.GetType().Name, owned.TenantId, expected, _correlation.CorrelationId);

            throw new TenantIsolationViolationException(
                entry.Entity.GetType().Name, expected, owned.TenantId);
        }

        if (entry.State != EntityState.Modified)
        {
            return;
        }

        var tenantProperty = entry.Property(nameof(ITenantOwned.TenantId));
        if (tenantProperty.OriginalValue is Guid original && original != expected)
        {
            _logger.LogCritical(
                "Tenant isolation violation: refused to re-tenant {EntityType} from {OriginalTenant} to {ExpectedTenant}. Correlation {CorrelationId}.",
                entry.Entity.GetType().Name, original, expected, _correlation.CorrelationId);

            throw new TenantIsolationViolationException(
                entry.Entity.GetType().Name, expected, original);
        }
    }

    /// <summary>Builds the audit row for one changed entity, or null when nothing meaningful changed.</summary>
    private AuditEvent? BuildAuditEvent(EntityEntry entry, DateTimeOffset now, Guid? actorId)
    {
        var entityType = entry.Entity.GetType().Name;

        var action = entry.State switch
        {
            EntityState.Added => AuditAction.Create,
            EntityState.Deleted => AuditAction.Archive,
            _ => AuditAction.Update
        };

        // Archiving is a soft delete implemented as an update; report it as an archive so the
        // trail reads the way a person expects.
        if (entry.State == EntityState.Modified
            && entry.Entity is ISoftArchivable
            && entry.Property(nameof(ISoftArchivable.IsArchived)) is { IsModified: true, CurrentValue: true })
        {
            action = AuditAction.Archive;
        }

        var before = new Dictionary<string, object?>(StringComparer.Ordinal);
        var after = new Dictionary<string, object?>(StringComparer.Ordinal);
        var changedFields = new List<string>();

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;

            if (IgnoredProperties.Contains(name))
            {
                continue;
            }

            var isRedacted = RedactedProperties.Contains(name);

            switch (entry.State)
            {
                case EntityState.Added:
                    after[name] = isRedacted ? "[redacted]" : property.CurrentValue;
                    break;

                case EntityState.Deleted:
                    before[name] = isRedacted ? "[redacted]" : property.OriginalValue;
                    break;

                default:
                    if (!property.IsModified || Equals(property.OriginalValue, property.CurrentValue))
                    {
                        continue;
                    }

                    changedFields.Add(name);
                    before[name] = isRedacted ? "[redacted]" : property.OriginalValue;
                    after[name] = isRedacted ? "[redacted]" : property.CurrentValue;
                    break;
            }
        }

        // An update where nothing actually changed is not worth a row.
        if (entry.State == EntityState.Modified && changedFields.Count == 0)
        {
            return null;
        }

        var tenantId = entry.Entity is ITenantOwned owned
            ? owned.TenantId
            : _tenant.HasTenant ? _tenant.TenantId : Guid.Empty;

        return new AuditEvent
        {
            TenantId = tenantId,
            OccurredAt = now,
            ActorUserId = actorId,
            ActorDisplayName = _currentUser.IsAuthenticated ? _currentUser.DisplayName : "System",
            ActorEmail = _currentUser.IsAuthenticated ? _currentUser.Email : null,
            Action = action,
            EntityType = entityType,
            EntityId = TryGetKey(entry),
            EntityLabel = TryGetLabel(entry.Entity),
            BeforeJson = before.Count == 0 ? null : Serialise(before),
            AfterJson = after.Count == 0 ? null : Serialise(after),
            ChangedFields = changedFields.Count == 0 ? null : Truncate(string.Join(',', changedFields), 2048),
            Source = AuditSource.Api,
            Outcome = AuditOutcome.Success,
            CorrelationId = _correlation.CorrelationId,
            IpAddress = _correlation.IpAddress,
            UserAgent = Truncate(_correlation.UserAgent, 512)
        };
    }

    private static string? TryGetKey(EntityEntry entry)
        => entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString();

    /// <summary>
    /// A human-readable label so the trail is legible without joining back to the record.
    /// Uses the record number where one exists, then a name, then a code.
    /// </summary>
    private static string? TryGetLabel(object entity)
    {
        var type = entity.GetType();

        foreach (var candidate in new[] { "Number", "Name", "DisplayName", "Code", "Title", "Key" })
        {
            var property = type.GetProperty(candidate);
            if (property?.PropertyType == typeof(string)
                && property.GetValue(entity) is string value
                && !string.IsNullOrWhiteSpace(value))
            {
                return Truncate(value, 256);
            }
        }

        return null;
    }

    private string Serialise(Dictionary<string, object?> values)
    {
        try
        {
            return Truncate(JsonSerializer.Serialize(values, JsonOptions), 32000)!;
        }
        catch (NotSupportedException ex)
        {
            // A property type the serialiser cannot handle must not break the write it describes.
            _logger.LogWarning(ex, "Could not serialise an audit snapshot; recording field names only.");
            return JsonSerializer.Serialize(
                new { unserialisable = true, fields = values.Keys.ToArray() },
                JsonOptions);
        }
    }

    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}
