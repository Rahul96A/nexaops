namespace NexaOps.Domain.Common;

/// <summary>
/// Raised when a caller attempts an operation the domain forbids - an illegal state
/// transition, a violated invariant. The API layer translates this to HTTP 409/422 with a
/// machine-readable <see cref="Code"/>, never a 500.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string message) : base(message) => Code = code;

    /// <summary>Stable, machine-readable identifier, e.g. <c>incident.invalid_transition</c>.</summary>
    public string Code { get; }
}

/// <summary>Raised when a requested record does not exist, or exists in another tenant.</summary>
public sealed class EntityNotFoundException : DomainException
{
    public EntityNotFoundException(string entityType, object id)
        : base("entity.not_found", $"{entityType} '{id}' was not found.")
    {
        EntityType = entityType;
        EntityId = id;
    }

    public string EntityType { get; }
    public object EntityId { get; }
}

/// <summary>
/// Raised when the ambient tenant does not match the tenant of a record being written.
/// This is a security event, not a validation error, and is always audited.
/// </summary>
public sealed class TenantIsolationViolationException : DomainException
{
    public TenantIsolationViolationException(string entityType, Guid expectedTenantId, Guid actualTenantId)
        : base("security.tenant_isolation_violation",
               $"Refused to persist {entityType}: record belongs to tenant {actualTenantId} " +
               $"but the ambient tenant is {expectedTenantId}.")
    {
        EntityType = entityType;
        ExpectedTenantId = expectedTenantId;
        ActualTenantId = actualTenantId;
    }

    public string EntityType { get; }
    public Guid ExpectedTenantId { get; }
    public Guid ActualTenantId { get; }
}
