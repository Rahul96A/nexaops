namespace NexaOps.Domain.Common;

/// <summary>
/// Marks an entity as belonging to exactly one tenant.
/// <para>
/// Every type implementing this interface is automatically given an EF Core global query
/// filter and is validated by the tenant guard interceptor on save. Adding this interface
/// to a new entity is the only thing required to bring it under tenant isolation, and
/// forgetting it is caught by <c>TenantIsolationTests.Every_tenant_scoped_entity_has_a_query_filter</c>.
/// </para>
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
