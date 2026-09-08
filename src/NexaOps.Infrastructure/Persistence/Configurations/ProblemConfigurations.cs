using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaOps.Domain.Problems;

namespace NexaOps.Infrastructure.Persistence.Configurations;

public sealed class ProblemConfiguration : IEntityTypeConfiguration<Problem>
{
    public void Configure(EntityTypeBuilder<Problem> builder)
    {
        builder.ToTable("Problems", "problem");

        builder.Property(x => x.Number).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(8000);

        // Investigation findings run long: a root cause worth publishing usually needs more than
        // a sentence, and truncating it would defeat the point of recording it.
        builder.Property(x => x.RootCause).HasMaxLength(8000);
        builder.Property(x => x.Workaround).HasMaxLength(8000);
        builder.Property(x => x.PermanentFix).HasMaxLength(8000);

        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.Priority).HasConversion<int>();
        builder.Property(x => x.Origin).HasConversion<int>();
        builder.Property(x => x.RootCauseConfidence).HasConversion<int>();

        builder.HasOne(x => x.AssignedTo).WithMany().HasForeignKey(x => x.AssignedToUserId);
        builder.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId);
        builder.HasOne(x => x.AssignmentGroup).WithMany().HasForeignKey(x => x.AssignmentGroupId);
        builder.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
        builder.HasOne(x => x.Subcategory).WithMany().HasForeignKey(x => x.SubcategoryId);

        builder.HasMany(x => x.Comments)
            .WithOne(x => x.Problem!)
            .HasForeignKey(x => x.ProblemId);

        builder.HasIndex(x => new { x.TenantId, x.Number })
            .IsUnique()
            .HasDatabaseName("UX_Problems_Tenant_Number");

        builder.HasIndex(x => new { x.TenantId, x.Status, x.Priority, x.CreatedAt })
            .HasDatabaseName("IX_Problems_Tenant_Status_Priority_Created");

        builder.HasIndex(x => new { x.TenantId, x.AssignedToUserId, x.Status })
            .HasDatabaseName("IX_Problems_Tenant_Assignee_Status");

        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.Status })
            .HasDatabaseName("IX_Problems_Tenant_Owner_Status");

        // Serves the known-error lookup an agent does mid-call: "is there already a workaround
        // for this?" It is the query that makes the module worth having.
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CategoryId })
            .HasDatabaseName("IX_Problems_Tenant_Status_Category");

        // Prioritisation: which problems are causing the most incidents.
        builder.HasIndex(x => new { x.TenantId, x.LinkedIncidentCount })
            .HasDatabaseName("IX_Problems_Tenant_LinkedIncidents");
    }
}

public sealed class ProblemCommentConfiguration : IEntityTypeConfiguration<ProblemComment>
{
    public void Configure(EntityTypeBuilder<ProblemComment> builder)
    {
        builder.ToTable("ProblemComments", "problem");

        builder.Property(x => x.Body).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.AuthorDisplayName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Kind).HasConversion<int>();

        builder.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);

        // Kind sits in the middle because work notes are filtered out at the query level for
        // callers without problem.worknote.read, so one index serves both filter and ordering.
        builder.HasIndex(x => new { x.ProblemId, x.Kind, x.CreatedAt })
            .HasDatabaseName("IX_ProblemComments_Problem_Kind_Created");
    }
}
