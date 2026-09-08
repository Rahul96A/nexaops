using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaOps.Domain.Assets;

namespace NexaOps.Infrastructure.Persistence.Configurations;

public sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("Assets", "asset");

        builder.Property(x => x.Number).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AssetTag).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Manufacturer).HasMaxLength(128);
        builder.Property(x => x.Model).HasMaxLength(128);
        builder.Property(x => x.SerialNumber).HasMaxLength(128);
        builder.Property(x => x.Location).HasMaxLength(200);
        builder.Property(x => x.Vendor).HasMaxLength(200);
        builder.Property(x => x.PurchaseOrderNumber).HasMaxLength(64);
        builder.Property(x => x.DisposalNotes).HasMaxLength(2000);

        builder.Property(x => x.Kind).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.PurchaseCost).HasPrecision(18, 2);

        builder.HasOne(x => x.AssignedTo).WithMany().HasForeignKey(x => x.AssignedToUserId);
        builder.HasOne(x => x.ConfigurationItem).WithMany().HasForeignKey(x => x.ConfigurationItemId);

        builder.HasMany(x => x.AssignmentHistory)
            .WithOne(x => x.Asset!)
            .HasForeignKey(x => x.AssetId);

        builder.HasIndex(x => new { x.TenantId, x.Number })
            .IsUnique()
            .HasDatabaseName("UX_Assets_Tenant_Number");

        // The asset tag is what a finance audit physically counts, so two assets sharing one
        // would make the register unreconcilable against the floor.
        builder.HasIndex(x => new { x.TenantId, x.AssetTag })
            .IsUnique()
            .HasDatabaseName("UX_Assets_Tenant_Tag");

        builder.HasIndex(x => new { x.TenantId, x.Status, x.Kind })
            .HasDatabaseName("IX_Assets_Tenant_Status_Kind");

        // "What do I have out" is the most-asked question of an asset register.
        builder.HasIndex(x => new { x.TenantId, x.AssignedToUserId })
            .HasDatabaseName("IX_Assets_Tenant_Assignee");

        builder.HasIndex(x => new { x.TenantId, x.WarrantyExpiresOn })
            .HasDatabaseName("IX_Assets_Tenant_WarrantyExpires");

        builder.HasIndex(x => new { x.TenantId, x.ConfigurationItemId })
            .HasDatabaseName("IX_Assets_Tenant_ConfigurationItem");
    }
}

public sealed class AssetAssignmentConfiguration : IEntityTypeConfiguration<AssetAssignment>
{
    public void Configure(EntityTypeBuilder<AssetAssignment> builder)
    {
        builder.ToTable("AssetAssignments", "asset");

        builder.Property(x => x.AssignmentNote).HasMaxLength(1000);
        builder.Property(x => x.ReturnNote).HasMaxLength(1000);

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);

        // Custody history, ordered as it is read: newest first for one asset.
        builder.HasIndex(x => new { x.AssetId, x.AssignedAt })
            .HasDatabaseName("IX_AssetAssignments_Asset_AssignedAt");

        // "What has this person held" - asked when somebody leaves.
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.ReturnedAt })
            .HasDatabaseName("IX_AssetAssignments_Tenant_User_Returned");
    }
}

public sealed class SoftwareLicenceConfiguration : IEntityTypeConfiguration<SoftwareLicence>
{
    public void Configure(EntityTypeBuilder<SoftwareLicence> builder)
    {
        builder.ToTable("SoftwareLicences", "asset");

        builder.Property(x => x.Number).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Publisher).HasMaxLength(200);
        builder.Property(x => x.Version).HasMaxLength(64);
        builder.Property(x => x.AgreementReference).HasMaxLength(128);
        builder.Property(x => x.Vendor).HasMaxLength(200);
        builder.Property(x => x.Notes).HasMaxLength(4000);

        builder.Property(x => x.Model).HasConversion<int>();
        builder.Property(x => x.AnnualCost).HasPrecision(18, 2);

        builder.HasIndex(x => new { x.TenantId, x.Number })
            .IsUnique()
            .HasDatabaseName("UX_SoftwareLicences_Tenant_Number");

        // Renewal is the deadline that costs money to miss.
        builder.HasIndex(x => new { x.TenantId, x.ExpiresOn })
            .HasDatabaseName("IX_SoftwareLicences_Tenant_Expires");

        builder.HasIndex(x => new { x.TenantId, x.ProductName })
            .HasDatabaseName("IX_SoftwareLicences_Tenant_Product");
    }
}
