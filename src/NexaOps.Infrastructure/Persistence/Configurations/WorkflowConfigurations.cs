using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaOps.Domain.Workflows;

namespace NexaOps.Infrastructure.Persistence.Configurations;

public sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("WorkflowDefinitions", "workflow");

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);

        builder.Property(x => x.Module).HasConversion<int>();
        builder.Property(x => x.Trigger).HasConversion<int>();

        // Conditions and actions have no life outside their rule, so they go with it. This is
        // the one cascade in the module: everything else — runs, records, notifications — is
        // history, and history is never deleted by deleting a rule.
        builder.HasMany(x => x.Conditions)
            .WithOne()
            .HasForeignKey(x => x.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Actions)
            .WithOne()
            .HasForeignKey(x => x.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Two rules cannot share a name within a tenant: the run history identifies a rule by
        // name, and two "Escalate P1s" would make that history ambiguous.
        builder.HasIndex(x => new { x.TenantId, x.Name })
            .IsUnique()
            .HasDatabaseName("UX_WorkflowDefinitions_Tenant_Name");

        // The engine's hot path: every record change asks for the active rules on one module
        // and trigger, and it asks on the same request the user is waiting on.
        builder.HasIndex(x => new { x.TenantId, x.Module, x.Trigger, x.IsActive })
            .HasDatabaseName("IX_WorkflowDefinitions_Tenant_Module_Trigger_Active");
    }
}

public sealed class WorkflowConditionConfiguration : IEntityTypeConfiguration<WorkflowCondition>
{
    public void Configure(EntityTypeBuilder<WorkflowCondition> builder)
    {
        builder.ToTable("WorkflowConditions", "workflow");

        builder.Property(x => x.Field).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(1000);
        builder.Property(x => x.Operator).HasConversion<int>();

        builder.HasIndex(x => new { x.TenantId, x.WorkflowDefinitionId })
            .HasDatabaseName("IX_WorkflowConditions_Tenant_Definition");
    }
}

public sealed class WorkflowActionConfiguration : IEntityTypeConfiguration<WorkflowAction>
{
    public void Configure(EntityTypeBuilder<WorkflowAction> builder)
    {
        builder.ToTable("WorkflowActions", "workflow");

        builder.Property(x => x.Message).HasMaxLength(1000);
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Recipient).HasConversion<int>();
        builder.Property(x => x.TargetPriority).HasConversion<int>();

        // No foreign keys to Users or Groups.
        //
        // A rule pointing at somebody who has since left should fail loudly at execution and be
        // reported in the run history, not block that person from being deactivated. A hard
        // constraint here would make offboarding depend on somebody remembering to tidy the
        // automation first.
        builder.HasIndex(x => new { x.TenantId, x.WorkflowDefinitionId, x.Sequence })
            .HasDatabaseName("IX_WorkflowActions_Tenant_Definition_Sequence");
    }
}

public sealed class WorkflowRunConfiguration : IEntityTypeConfiguration<WorkflowRun>
{
    public void Configure(EntityTypeBuilder<WorkflowRun> builder)
    {
        builder.ToTable("WorkflowRuns", "workflow");

        builder.Property(x => x.WorkflowName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RecordNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(1000);

        builder.Property(x => x.Module).HasConversion<int>();
        builder.Property(x => x.Trigger).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();

        builder.HasMany(x => x.Steps)
            .WithOne()
            .HasForeignKey(x => x.WorkflowRunId)
            .OnDelete(DeleteBehavior.Cascade);

        // No foreign key to the definition. Deleting a rule is not supported today, but a run
        // describes what happened at the time — including the rule's name as it then was — and
        // it should stay readable whatever becomes of the rule.
        builder.HasIndex(x => new { x.TenantId, x.WorkflowDefinitionId, x.StartedAt })
            .HasDatabaseName("IX_WorkflowRuns_Tenant_Definition_Started");

        // "What has automation done to this ticket" is asked from a record page.
        builder.HasIndex(x => new { x.TenantId, x.RecordId })
            .HasDatabaseName("IX_WorkflowRuns_Tenant_Record");

        builder.HasIndex(x => new { x.TenantId, x.Status, x.StartedAt })
            .HasDatabaseName("IX_WorkflowRuns_Tenant_Status_Started");
    }
}

public sealed class WorkflowStepRunConfiguration : IEntityTypeConfiguration<WorkflowStepRun>
{
    public void Configure(EntityTypeBuilder<WorkflowStepRun> builder)
    {
        builder.ToTable("WorkflowStepRuns", "workflow");

        builder.Property(x => x.Detail).HasMaxLength(1000);
        builder.Property(x => x.Error).HasMaxLength(2000);

        builder.Property(x => x.ActionType).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();

        builder.HasIndex(x => new { x.TenantId, x.WorkflowRunId })
            .HasDatabaseName("IX_WorkflowStepRuns_Tenant_Run");
    }
}
