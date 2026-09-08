using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaOps.Domain.Cmdb;

namespace NexaOps.Infrastructure.Persistence.Configurations;

public sealed class ConfigurationItemConfiguration : IEntityTypeConfiguration<ConfigurationItem>
{
    public void Configure(EntityTypeBuilder<ConfigurationItem> builder)
    {
        builder.ToTable("ConfigurationItems", "cmdb");

        builder.Property(x => x.Number).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Location).HasMaxLength(200);
        builder.Property(x => x.SerialNumber).HasMaxLength(128);
        builder.Property(x => x.Manufacturer).HasMaxLength(128);
        builder.Property(x => x.Model).HasMaxLength(128);
        builder.Property(x => x.Version).HasMaxLength(64);
        builder.Property(x => x.Environment).HasMaxLength(64);
        builder.Property(x => x.Vendor).HasMaxLength(200);

        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.Criticality).HasConversion<int>();

        builder.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId);
        builder.HasOne(x => x.SupportGroup).WithMany().HasForeignKey(x => x.SupportGroupId);

        builder.HasIndex(x => new { x.TenantId, x.Number })
            .IsUnique()
            .HasDatabaseName("UX_ConfigurationItems_Tenant_Number");

        // The name is how people find a CI - they know "sql-prod-01", not CI0000042 - so it is
        // unique per tenant and indexed for lookup.
        builder.HasIndex(x => new { x.TenantId, x.Name })
            .IsUnique()
            .HasDatabaseName("UX_ConfigurationItems_Tenant_Name");

        builder.HasIndex(x => new { x.TenantId, x.Type, x.Status })
            .HasDatabaseName("IX_ConfigurationItems_Tenant_Type_Status");

        builder.HasIndex(x => new { x.TenantId, x.Criticality, x.Status })
            .HasDatabaseName("IX_ConfigurationItems_Tenant_Criticality_Status");

        // Serves the question worth asking before an outage rather than during one: what is
        // about to fall out of support?
        builder.HasIndex(x => new { x.TenantId, x.SupportExpiresOn })
            .HasDatabaseName("IX_ConfigurationItems_Tenant_SupportExpires");

        builder.HasIndex(x => new { x.TenantId, x.SupportGroupId })
            .HasDatabaseName("IX_ConfigurationItems_Tenant_SupportGroup");
    }
}

public sealed class CiRelationshipConfiguration : IEntityTypeConfiguration<CiRelationship>
{
    public void Configure(EntityTypeBuilder<CiRelationship> builder)
    {
        builder.ToTable("CiRelationships", "cmdb");

        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Type).HasConversion<int>();

        builder.HasOne(x => x.Source)
            .WithMany(x => x.OutgoingRelationships)
            .HasForeignKey(x => x.SourceId);

        // Configured without an inverse navigation: the reverse direction is answered by
        // querying on TargetId, and a second collection would invite the two to disagree.
        builder.HasOne(x => x.Target)
            .WithMany()
            .HasForeignKey(x => x.TargetId);

        // One edge of a given type between two items. A duplicate would double-count in impact
        // analysis without adding information.
        builder.HasIndex(x => new { x.SourceId, x.TargetId, x.Type })
            .IsUnique()
            .HasDatabaseName("UX_CiRelationships_Source_Target_Type");

        // Both directions are indexed because impact analysis walks one way and dependency
        // analysis the other, and both run on a record page.
        builder.HasIndex(x => new { x.TenantId, x.SourceId })
            .HasDatabaseName("IX_CiRelationships_Tenant_Source");

        builder.HasIndex(x => new { x.TenantId, x.TargetId })
            .HasDatabaseName("IX_CiRelationships_Tenant_Target");
    }
}
