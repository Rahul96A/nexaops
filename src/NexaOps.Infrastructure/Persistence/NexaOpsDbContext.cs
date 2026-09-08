using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Assets;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Catalog;
using NexaOps.Domain.Changes;
using NexaOps.Domain.Cmdb;
using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;
using NexaOps.Domain.Knowledge;
using NexaOps.Domain.Platform;
using NexaOps.Domain.Problems;
using NexaOps.Domain.Requests;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Infrastructure.Persistence;

/// <summary>
/// The single database context for the modular monolith.
/// <para>
/// Tenant isolation is applied here rather than being left to callers: every entity implementing
/// <see cref="ITenantOwned"/> is given a global query filter automatically in
/// <see cref="OnModelCreating"/>, so a repository that forgets to filter still cannot read
/// another tenant's rows. Writes are policed separately by the tenant guard in
/// <see cref="Interceptors.AuditAndTenantInterceptor"/>.
/// </para>
/// </summary>
public class NexaOpsDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// A single constructor by design. <see cref="IDbContextFactory{TContext}"/> resolves the
    /// context through <c>ActivatorUtilities</c>, which refuses to choose between two
    /// constructors that both match - so callers with no tenant (design-time tooling, tests)
    /// pass <see cref="NoTenantContext.Instance"/> rather than getting an overload of their own.
    /// </summary>
    public NexaOpsDbContext(DbContextOptions<NexaOpsDbContext> options, ITenantContext tenantContext)
        : base(options)
        => _tenantContext = tenantContext;

    /// <summary>
    /// The tenant every query is filtered to.
    /// <para>
    /// EF Core turns this into a query parameter evaluated at execution time, so a background
    /// worker that switches tenant part-way through a scope gets correctly re-filtered queries
    /// rather than a value captured when the context was constructed.
    /// </para>
    /// <para>
    /// When no tenant is established this is <see cref="Guid.Empty"/>, which matches no row.
    /// Failing closed is deliberate: an un-scoped query must return nothing, not everything.
    /// </para>
    /// </summary>
    public Guid CurrentTenantId
        => _tenantContext.HasTenant ? _tenantContext.TenantId : Guid.Empty;

    /// <summary>
    /// True while tenant filtering is suppressed for genuinely cross-tenant work: the background
    /// SLA monitor scanning every tenant's clocks, tenant provisioning, and sign-in, which all
    /// run before or across tenant scopes.
    /// <para>
    /// Readable by infrastructure code but only settable through
    /// <see cref="SuppressTenantFilter"/>, which restores the previous value on dispose. An
    /// earlier version exposed a plain setter and a nested call reset it to false on the way
    /// out, silently un-suppressing an outer scope - a scope object makes that mistake
    /// impossible to write.
    /// </para>
    /// </summary>
    internal bool IgnoreTenantFilter { get; private set; }

    /// <summary>
    /// Suppresses tenant filtering until the returned token is disposed, restoring whatever the
    /// previous state was. Never reachable from a controller or an application service.
    /// </summary>
    internal IDisposable SuppressTenantFilter()
    {
        var previous = IgnoreTenantFilter;
        IgnoreTenantFilter = true;
        return new TenantFilterSuppression(this, previous);
    }

    /// <summary>
    /// True while automatic audit capture is suppressed for bulk provisioning.
    /// <para>
    /// Set only through <see cref="SuppressAuditCapture"/>. Tenant stamping and the tenant guard
    /// still run - this suppresses the before/after snapshot rows only.
    /// </para>
    /// </summary>
    internal bool SuppressAudit { get; private set; }

    /// <summary>
    /// Suppresses automatic audit capture until the returned token is disposed.
    /// <para>
    /// Intended for bulk provisioning and demo seeding, where every inserted row would otherwise
    /// produce an audit event with a full JSON snapshot. That is both meaningless - nobody
    /// performed those actions - and expensive enough to dominate the write volume. Deliberate
    /// administrative actions must never use this: the audit trail is the record of what people
    /// did, and seeding is not something a person did.
    /// </para>
    /// </summary>
    internal IDisposable SuppressAuditCapture()
    {
        var previous = SuppressAudit;
        SuppressAudit = true;
        return new AuditSuppression(this, previous);
    }

    private sealed class AuditSuppression : IDisposable
    {
        private readonly NexaOpsDbContext _context;
        private readonly bool _previous;
        private bool _disposed;

        public AuditSuppression(NexaOpsDbContext context, bool previous)
        {
            _context = context;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _context.SuppressAudit = _previous;
            _disposed = true;
        }
    }

    private sealed class TenantFilterSuppression : IDisposable
    {
        private readonly NexaOpsDbContext _context;
        private readonly bool _previous;
        private bool _disposed;

        public TenantFilterSuppression(NexaOpsDbContext context, bool previous)
        {
            _context = context;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _context.IgnoreTenantFilter = _previous;
            _disposed = true;
        }
    }

    // --- Identity ---
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // --- Audit ---
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    // --- Service desk ---
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentComment> IncidentComments => Set<IncidentComment>();
    public DbSet<IncidentTag> IncidentTags => Set<IncidentTag>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Subcategory> Subcategories => Set<Subcategory>();
    public DbSet<RecordRelation> RecordRelations => Set<RecordRelation>();
    public DbSet<PriorityMatrixEntry> PriorityMatrixEntries => Set<PriorityMatrixEntry>();

    // --- SLA ---
    public DbSet<BusinessCalendar> BusinessCalendars => Set<BusinessCalendar>();
    public DbSet<BusinessCalendarWindow> BusinessCalendarWindows => Set<BusinessCalendarWindow>();
    public DbSet<BusinessCalendarHoliday> BusinessCalendarHolidays => Set<BusinessCalendarHoliday>();
    public DbSet<SlaDefinition> SlaDefinitions => Set<SlaDefinition>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<SlaInstance> SlaInstances => Set<SlaInstance>();

    // --- Platform ---
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    // --- Service catalogue, requests and approvals ---
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<CatalogItemVariable> CatalogItemVariables => Set<CatalogItemVariable>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<RequestItem> RequestItems => Set<RequestItem>();
    public DbSet<RequestComment> RequestComments => Set<RequestComment>();
    public DbSet<Approval> Approvals => Set<Approval>();

    // --- Problem management ---
    public DbSet<Problem> Problems => Set<Problem>();
    public DbSet<ProblemComment> ProblemComments => Set<ProblemComment>();

    // --- Change management ---
    public DbSet<Change> Changes => Set<Change>();
    public DbSet<ChangeComment> ChangeComments => Set<ChangeComment>();

    // --- Knowledge base ---
    public DbSet<KnowledgeArticle> Articles => Set<KnowledgeArticle>();
    public DbSet<ArticleFeedback> ArticleFeedback => Set<ArticleFeedback>();

    // --- Configuration management database ---
    public DbSet<ConfigurationItem> ConfigurationItems => Set<ConfigurationItem>();
    public DbSet<CiRelationship> CiRelationships => Set<CiRelationship>();

    // --- Asset management ---
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetAssignment> AssetAssignments => Set<AssetAssignment>();
    public DbSet<SoftwareLicence> SoftwareLicences => Set<SoftwareLicence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NexaOpsDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);
        ApplyGlobalConventions(modelBuilder);
    }

    /// <summary>
    /// Adds a tenant query filter to every <see cref="ITenantOwned"/> entity by reflection.
    /// <para>
    /// Doing this by convention rather than per-entity is the point: a developer adding a new
    /// tenant-owned entity gets isolation for free, and cannot forget it. The companion test
    /// <c>Every_tenant_owned_entity_has_a_tenant_query_filter</c> fails the build if this
    /// convention ever stops covering an entity.
    /// </para>
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType) || entityType.BaseType is not null)
            {
                continue;
            }

            var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");

            var tenantProperty = System.Linq.Expressions.Expression.Property(
                parameter,
                nameof(ITenantOwned.TenantId));

            // Reference the context's own properties so EF re-evaluates them per query.
            var contextConstant = System.Linq.Expressions.Expression.Constant(this);

            var currentTenant = System.Linq.Expressions.Expression.Property(
                contextConstant,
                nameof(CurrentTenantId));

            var ignoreFilter = System.Linq.Expressions.Expression.Property(
                contextConstant,
                nameof(IgnoreTenantFilter));

            // e => IgnoreTenantFilter || e.TenantId == CurrentTenantId
            var body = System.Linq.Expressions.Expression.OrElse(
                ignoreFilter,
                System.Linq.Expressions.Expression.Equal(tenantProperty, currentTenant));

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(System.Linq.Expressions.Expression.Lambda(body, parameter));
        }
    }

    /// <summary>
    /// Conventions applied to every entity: sane string lengths, decimal precision, and
    /// restrictive delete behaviour.
    /// </summary>
    private static void ApplyGlobalConventions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                // An unbounded nvarchar(max) column cannot be indexed and invites abuse.
                // Anything that genuinely needs more says so explicitly in its configuration.
                if (property.ClrType == typeof(string) && property.GetMaxLength() is null)
                {
                    property.SetMaxLength(512);
                }

                // Money and quantities are decimal, never floating point.
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                {
                    property.SetPrecision(18);
                    property.SetScale(4);
                }
            }

            // Never cascade a delete. Business records are archived, not deleted, and a stray
            // cascade across a tenant boundary would be catastrophic and silent.
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }
}
