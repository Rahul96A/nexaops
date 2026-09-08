using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Infrastructure.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", "servicedesk");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1024);
        builder.Property(x => x.Module).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.TenantId, x.Module, x.Code })
            .IsUnique()
            .HasDatabaseName("UX_Categories_Tenant_Module_Code");
    }
}

public sealed class SubcategoryConfiguration : IEntityTypeConfiguration<Subcategory>
{
    public void Configure(EntityTypeBuilder<Subcategory> builder)
    {
        builder.ToTable("Subcategories", "servicedesk");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1024);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Category)
            .WithMany(c => c.Subcategories)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CategoryId, x.Code })
            .IsUnique()
            .HasDatabaseName("UX_Subcategories_Category_Code");
    }
}

public sealed class PriorityMatrixEntryConfiguration : IEntityTypeConfiguration<PriorityMatrixEntry>
{
    public void Configure(EntityTypeBuilder<PriorityMatrixEntry> builder)
    {
        builder.ToTable("PriorityMatrix", "servicedesk");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Impact).HasConversion<int>();
        builder.Property(x => x.Urgency).HasConversion<int>();
        builder.Property(x => x.Priority).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.TenantId, x.Impact, x.Urgency })
            .IsUnique()
            .HasDatabaseName("UX_PriorityMatrix_Tenant_Impact_Urgency");
    }
}

public sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("Incidents", "servicedesk");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Number).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();

        // Descriptions and resolution notes are genuinely long-form free text.
        builder.Property(x => x.Description).HasMaxLength(20000).IsRequired();
        builder.Property(x => x.ResolutionNotes).HasMaxLength(20000);
        builder.Property(x => x.PriorityOverrideReason).HasMaxLength(1000);

        builder.Property(x => x.Impact).HasConversion<int>();
        builder.Property(x => x.Urgency).HasConversion<int>();
        builder.Property(x => x.Priority).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.PendingReason).HasConversion<int>();
        builder.Property(x => x.Channel).HasConversion<int>();
        builder.Property(x => x.ResolutionCode).HasConversion<int>();

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Requester).WithMany()
            .HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AffectedUser).WithMany()
            .HasForeignKey(x => x.AffectedUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AssignedTo).WithMany()
            .HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AssignmentGroup).WithMany()
            .HasForeignKey(x => x.AssignmentGroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Category).WithMany()
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Subcategory).WithMany()
            .HasForeignKey(x => x.SubcategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ParentIncident).WithMany()
            .HasForeignKey(x => x.ParentIncidentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Comments)
            .WithOne(c => c.Incident)
            .HasForeignKey(c => c.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Tags)
            .WithOne(t => t.Incident)
            .HasForeignKey(t => t.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);

        // SLA instances are polymorphic across modules, so the relationship is configured from
        // the incident side only, keyed on RecordId with a module discriminator in the query.
        builder.HasMany(x => x.SlaInstances)
            .WithOne()
            .HasForeignKey(s => s.RecordId)
            .HasPrincipalKey(i => i.Id)
            .OnDelete(DeleteBehavior.Cascade);

        // --- Indexes chosen from the queries the product actually runs. ---

        builder.HasIndex(x => new { x.TenantId, x.Number })
            .IsUnique()
            .HasDatabaseName("UX_Incidents_Tenant_Number");

        // The default service desk queue: open work for a tenant, newest first.
        builder.HasIndex(x => new { x.TenantId, x.Status, x.Priority, x.CreatedAt })
            .HasDatabaseName("IX_Incidents_Tenant_Status_Priority_Created");

        // "My work" and per-agent workload.
        builder.HasIndex(x => new { x.TenantId, x.AssignedToUserId, x.Status })
            .HasDatabaseName("IX_Incidents_Tenant_Assignee_Status");

        // Team queues and the unassigned pool.
        builder.HasIndex(x => new { x.TenantId, x.AssignmentGroupId, x.Status })
            .HasDatabaseName("IX_Incidents_Tenant_Group_Status");

        // Requester-facing "my tickets".
        builder.HasIndex(x => new { x.TenantId, x.RequesterId, x.Status })
            .HasDatabaseName("IX_Incidents_Tenant_Requester_Status");

        // SLA monitoring and the "due soon" view.
        builder.HasIndex(x => new { x.TenantId, x.NextSlaDueAt })
            .HasDatabaseName("IX_Incidents_Tenant_NextSlaDue");

        builder.HasIndex(x => new { x.TenantId, x.HasBreachedSla, x.Status })
            .HasDatabaseName("IX_Incidents_Tenant_Breached_Status");

        builder.HasIndex(x => new { x.TenantId, x.CategoryId })
            .HasDatabaseName("IX_Incidents_Tenant_Category");

        builder.HasIndex(x => new { x.TenantId, x.CreatedAt })
            .HasDatabaseName("IX_Incidents_Tenant_CreatedAt");
    }
}

public sealed class IncidentCommentConfiguration : IEntityTypeConfiguration<IncidentComment>
{
    public void Configure(EntityTypeBuilder<IncidentComment> builder)
    {
        builder.ToTable("IncidentComments", "servicedesk");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Body).HasMaxLength(20000).IsRequired();
        builder.Property(x => x.Kind).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Author).WithMany()
            .HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);

        // The timeline reads comments for one incident in creation order, filtered by kind for
        // callers who may not see work notes.
        builder.HasIndex(x => new { x.IncidentId, x.Kind, x.CreatedAt })
            .HasDatabaseName("IX_IncidentComments_Incident_Kind_Created");
    }
}

public sealed class IncidentTagConfiguration : IEntityTypeConfiguration<IncidentTag>
{
    public void Configure(EntityTypeBuilder<IncidentTag> builder)
    {
        builder.ToTable("IncidentTags", "servicedesk");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Tag).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.IncidentId, x.Tag })
            .IsUnique()
            .HasDatabaseName("UX_IncidentTags_Incident_Tag");

        builder.HasIndex(x => new { x.TenantId, x.Tag }).HasDatabaseName("IX_IncidentTags_Tenant_Tag");
    }
}

public sealed class RecordRelationConfiguration : IEntityTypeConfiguration<RecordRelation>
{
    public void Configure(EntityTypeBuilder<RecordRelation> builder)
    {
        builder.ToTable("RecordRelations", "servicedesk");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceModule).HasConversion<int>();
        builder.Property(x => x.TargetModule).HasConversion<int>();
        builder.Property(x => x.RelationType).HasConversion<int>();
        builder.Property(x => x.Note).HasMaxLength(1024);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.TenantId, x.SourceModule, x.SourceId })
            .HasDatabaseName("IX_RecordRelations_Source");

        builder.HasIndex(x => new { x.TenantId, x.TargetModule, x.TargetId })
            .HasDatabaseName("IX_RecordRelations_Target");

        builder.HasIndex(x => new { x.SourceModule, x.SourceId, x.TargetModule, x.TargetId, x.RelationType })
            .IsUnique()
            .HasDatabaseName("UX_RecordRelations_Pair");
    }
}
