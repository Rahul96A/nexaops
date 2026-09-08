using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Catalog;
using NexaOps.Domain.Requests;

namespace NexaOps.Infrastructure.Persistence.Configurations;

public sealed class CatalogItemConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("CatalogItems", "catalog");

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ShortDescription).HasMaxLength(400);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.Icon).HasMaxLength(64);
        builder.Property(x => x.Cost).HasPrecision(18, 2);

        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.Priority).HasConversion<int>();
        builder.Property(x => x.ApprovalTargetKind).HasConversion<int>();
        builder.Property(x => x.ApprovalRule).HasConversion<int>();

        builder.HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId);

        builder.HasOne(x => x.FulfilmentGroup)
            .WithMany()
            .HasForeignKey(x => x.FulfilmentGroupId);

        builder.HasMany(x => x.Variables)
            .WithOne(x => x.CatalogItem!)
            .HasForeignKey(x => x.CatalogItemId);

        builder.HasIndex(x => new { x.TenantId, x.Code })
            .IsUnique()
            .HasDatabaseName("UX_CatalogItems_Tenant_Code");

        // The catalogue browse page filters on status and orders within a category.
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CategoryId, x.SortOrder })
            .HasDatabaseName("IX_CatalogItems_Tenant_Status_Category_Sort");
    }
}

public sealed class CatalogItemVariableConfiguration : IEntityTypeConfiguration<CatalogItemVariable>
{
    public void Configure(EntityTypeBuilder<CatalogItemVariable> builder)
    {
        builder.ToTable("CatalogItemVariables", "catalog");

        builder.Property(x => x.Key).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.HelpText).HasMaxLength(1000);
        builder.Property(x => x.DefaultValue).HasMaxLength(1000);
        builder.Property(x => x.ChoicesJson).HasMaxLength(4000);
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.MinValue).HasPrecision(18, 4);
        builder.Property(x => x.MaxValue).HasPrecision(18, 4);

        // A duplicate key would make one answer silently overwrite another when the submitted
        // values are serialised into a single JSON object.
        builder.HasIndex(x => new { x.CatalogItemId, x.Key })
            .IsUnique()
            .HasDatabaseName("UX_CatalogItemVariables_Item_Key");
    }
}

public sealed class ServiceRequestConfiguration : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> builder)
    {
        builder.ToTable("ServiceRequests", "request");

        builder.Property(x => x.Number).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(8000);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
        builder.Property(x => x.CancellationReason).HasMaxLength(1000);
        builder.Property(x => x.TotalCost).HasPrecision(18, 2);

        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.PendingReason).HasConversion<int>();
        builder.Property(x => x.Channel).HasConversion<int>();
        builder.Property(x => x.Priority).HasConversion<int>();

        builder.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId);
        builder.HasOne(x => x.RequestedFor).WithMany().HasForeignKey(x => x.RequestedForId);
        builder.HasOne(x => x.AssignedTo).WithMany().HasForeignKey(x => x.AssignedToUserId);
        builder.HasOne(x => x.FulfilmentGroup).WithMany().HasForeignKey(x => x.FulfilmentGroupId);
        builder.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);

        builder.HasMany(x => x.Items)
            .WithOne(x => x.ServiceRequest!)
            .HasForeignKey(x => x.ServiceRequestId);

        builder.HasMany(x => x.Comments)
            .WithOne(x => x.ServiceRequest!)
            .HasForeignKey(x => x.ServiceRequestId);

        // SLA clocks and approvals are both addressed by Module + RecordId so that later
        // modules reuse them without a schema change. Mapping either as a navigation makes EF
        // add a ServiceRequestId foreign key, which is exactly the coupling those tables exist
        // to avoid. Both are loaded explicitly by their repositories instead.
        builder.Ignore(x => x.SlaInstances);
        builder.Ignore(x => x.Approvals);

        builder.HasIndex(x => new { x.TenantId, x.Number })
            .IsUnique()
            .HasDatabaseName("UX_ServiceRequests_Tenant_Number");

        // Every index leads with TenantId, because every query is tenant-filtered and one that
        // did not would be unusable.
        builder.HasIndex(x => new { x.TenantId, x.Status, x.Priority, x.CreatedAt })
            .HasDatabaseName("IX_ServiceRequests_Tenant_Status_Priority_Created");

        builder.HasIndex(x => new { x.TenantId, x.AssignedToUserId, x.Status })
            .HasDatabaseName("IX_ServiceRequests_Tenant_Assignee_Status");

        builder.HasIndex(x => new { x.TenantId, x.FulfilmentGroupId, x.Status })
            .HasDatabaseName("IX_ServiceRequests_Tenant_Group_Status");

        builder.HasIndex(x => new { x.TenantId, x.RequesterId, x.Status })
            .HasDatabaseName("IX_ServiceRequests_Tenant_Requester_Status");

        // Serves the "raised for me" view, which is distinct from "raised by me" whenever a
        // manager orders on somebody's behalf.
        builder.HasIndex(x => new { x.TenantId, x.RequestedForId, x.Status })
            .HasDatabaseName("IX_ServiceRequests_Tenant_RequestedFor_Status");

        builder.HasIndex(x => new { x.TenantId, x.NextSlaDueAt })
            .HasDatabaseName("IX_ServiceRequests_Tenant_NextSlaDue");

        builder.HasIndex(x => new { x.TenantId, x.HasBreachedSla, x.Status })
            .HasDatabaseName("IX_ServiceRequests_Tenant_Breached_Status");
    }
}

public sealed class RequestItemConfiguration : IEntityTypeConfiguration<RequestItem>
{
    public void Configure(EntityTypeBuilder<RequestItem> builder)
    {
        builder.ToTable("RequestItems", "request");

        builder.Property(x => x.CatalogItemName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.UnitCost).HasPrecision(18, 2);
        builder.Property(x => x.FulfilmentNotes).HasMaxLength(2000);
        builder.Property(x => x.Status).HasConversion<int>();

        // The answers are a JSON document whose shape is defined by the catalogue item, not by
        // the schema. Nothing queries across individual answers, so rows would buy nothing.
        builder.Property(x => x.VariableValuesJson).HasMaxLength(8000).IsRequired();

        builder.HasOne(x => x.CatalogItem).WithMany().HasForeignKey(x => x.CatalogItemId);
        builder.HasOne(x => x.FulfilmentGroup).WithMany().HasForeignKey(x => x.FulfilmentGroupId);
        builder.HasOne(x => x.AssignedTo).WithMany().HasForeignKey(x => x.AssignedToUserId);

        builder.HasIndex(x => new { x.ServiceRequestId, x.Status })
            .HasDatabaseName("IX_RequestItems_Request_Status");

        // Supports "how often is this item ordered", which is the first question asked of a
        // catalogue once it has been running for a quarter.
        builder.HasIndex(x => new { x.TenantId, x.CatalogItemId })
            .HasDatabaseName("IX_RequestItems_Tenant_CatalogItem");
    }
}

public sealed class RequestCommentConfiguration : IEntityTypeConfiguration<RequestComment>
{
    public void Configure(EntityTypeBuilder<RequestComment> builder)
    {
        builder.ToTable("RequestComments", "request");

        builder.Property(x => x.Body).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.AuthorDisplayName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Kind).HasConversion<int>();

        builder.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);

        // Kind sits in the middle because work notes are filtered out at the query level for
        // callers without request.worknote.read, so one index serves both filter and ordering.
        builder.HasIndex(x => new { x.ServiceRequestId, x.Kind, x.CreatedAt })
            .HasDatabaseName("IX_RequestComments_Request_Kind_Created");
    }
}

public sealed class ApprovalConfiguration : IEntityTypeConfiguration<Approval>
{
    public void Configure(EntityTypeBuilder<Approval> builder)
    {
        builder.ToTable("Approvals", "approval");

        builder.Property(x => x.Module).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RecordLabel).HasMaxLength(400).IsRequired();
        builder.Property(x => x.Comment).HasMaxLength(2000);

        builder.Property(x => x.State).HasConversion<int>();
        builder.Property(x => x.Rule).HasConversion<int>();
        builder.Property(x => x.TargetKind).HasConversion<int>();

        builder.HasOne(x => x.ApproverUser).WithMany().HasForeignKey(x => x.ApproverUserId);
        builder.HasOne(x => x.ApproverGroup).WithMany().HasForeignKey(x => x.ApproverGroupId);

        // Module + RecordId rather than a foreign key to ServiceRequest, so change management
        // reuses this table without a schema change.
        builder.HasIndex(x => new { x.TenantId, x.Module, x.RecordId, x.Stage })
            .HasDatabaseName("IX_Approvals_Tenant_Record_Stage");

        // The approver's own queue: "what is waiting on me".
        builder.HasIndex(x => new { x.TenantId, x.ApproverUserId, x.State })
            .HasDatabaseName("IX_Approvals_Tenant_Approver_State");

        builder.HasIndex(x => new { x.TenantId, x.ApproverGroupId, x.State })
            .HasDatabaseName("IX_Approvals_Tenant_ApproverGroup_State");
    }
}
