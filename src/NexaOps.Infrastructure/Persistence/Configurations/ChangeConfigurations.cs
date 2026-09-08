using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaOps.Domain.Changes;

namespace NexaOps.Infrastructure.Persistence.Configurations;

public sealed class ChangeConfiguration : IEntityTypeConfiguration<Change>
{
    public void Configure(EntityTypeBuilder<Change> builder)
    {
        builder.ToTable("Changes", "change");

        builder.Property(x => x.Number).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(8000);

        // Plans run long. A rollback plan that has been truncated is worse than none, because it
        // reads as if somebody thought about it.
        builder.Property(x => x.ImplementationPlan).HasMaxLength(8000);
        builder.Property(x => x.RollbackPlan).HasMaxLength(8000);
        builder.Property(x => x.TestPlan).HasMaxLength(8000);
        builder.Property(x => x.ImpactAssessment).HasMaxLength(8000);
        builder.Property(x => x.ReviewNotes).HasMaxLength(8000);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
        builder.Property(x => x.CancellationReason).HasMaxLength(1000);

        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.Risk).HasConversion<int>();
        builder.Property(x => x.Impact).HasConversion<int>();
        builder.Property(x => x.Priority).HasConversion<int>();
        builder.Property(x => x.Outcome).HasConversion<int>();

        builder.HasOne(x => x.AssignedTo).WithMany().HasForeignKey(x => x.AssignedToUserId);
        builder.HasOne(x => x.RequestedBy).WithMany().HasForeignKey(x => x.RequestedByUserId);
        builder.HasOne(x => x.AssignmentGroup).WithMany().HasForeignKey(x => x.AssignmentGroupId);
        builder.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);

        builder.HasMany(x => x.Comments)
            .WithOne(x => x.Change!)
            .HasForeignKey(x => x.ChangeId);

        builder.HasIndex(x => new { x.TenantId, x.Number })
            .IsUnique()
            .HasDatabaseName("UX_Changes_Tenant_Number");

        builder.HasIndex(x => new { x.TenantId, x.Status, x.Type, x.PlannedStartAt })
            .HasDatabaseName("IX_Changes_Tenant_Status_Type_PlannedStart");

        builder.HasIndex(x => new { x.TenantId, x.AssignedToUserId, x.Status })
            .HasDatabaseName("IX_Changes_Tenant_Assignee_Status");

        // The change calendar - "what is happening this week" - is the single most-used view in
        // the module, and it is a window query.
        builder.HasIndex(x => new { x.TenantId, x.PlannedStartAt, x.PlannedEndAt })
            .HasDatabaseName("IX_Changes_Tenant_Window");

        builder.HasIndex(x => new { x.TenantId, x.ProblemId })
            .HasDatabaseName("IX_Changes_Tenant_Problem");
    }
}

public sealed class ChangeCommentConfiguration : IEntityTypeConfiguration<ChangeComment>
{
    public void Configure(EntityTypeBuilder<ChangeComment> builder)
    {
        builder.ToTable("ChangeComments", "change");

        builder.Property(x => x.Body).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.AuthorDisplayName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Kind).HasConversion<int>();

        builder.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);

        builder.HasIndex(x => new { x.ChangeId, x.Kind, x.CreatedAt })
            .HasDatabaseName("IX_ChangeComments_Change_Kind_Created");
    }
}
