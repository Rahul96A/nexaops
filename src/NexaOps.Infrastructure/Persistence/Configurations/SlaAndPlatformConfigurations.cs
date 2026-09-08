using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Platform;
using NexaOps.Domain.Sla;

namespace NexaOps.Infrastructure.Persistence.Configurations;

public sealed class BusinessCalendarConfiguration : IEntityTypeConfiguration<BusinessCalendar>
{
    public void Configure(EntityTypeBuilder<BusinessCalendar> builder)
    {
        builder.ToTable("BusinessCalendars", "sla");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1024);
        builder.Property(x => x.TimeZoneId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasMany(x => x.Windows).WithOne(w => w.BusinessCalendar)
            .HasForeignKey(w => w.BusinessCalendarId).OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Holidays).WithOne(h => h.BusinessCalendar)
            .HasForeignKey(h => h.BusinessCalendarId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique()
            .HasDatabaseName("UX_BusinessCalendars_Tenant_Code");
    }
}

public sealed class BusinessCalendarWindowConfiguration : IEntityTypeConfiguration<BusinessCalendarWindow>
{
    public void Configure(EntityTypeBuilder<BusinessCalendarWindow> builder)
    {
        builder.ToTable("BusinessCalendarWindows", "sla");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DayOfWeek).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.BusinessCalendarId, x.DayOfWeek, x.StartMinute })
            .IsUnique()
            .HasDatabaseName("UX_CalendarWindows_Calendar_Day_Start");
    }
}

public sealed class BusinessCalendarHolidayConfiguration : IEntityTypeConfiguration<BusinessCalendarHoliday>
{
    public void Configure(EntityTypeBuilder<BusinessCalendarHoliday> builder)
    {
        builder.ToTable("BusinessCalendarHolidays", "sla");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Date).HasColumnType("date");
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.BusinessCalendarId, x.Date })
            .IsUnique()
            .HasDatabaseName("UX_CalendarHolidays_Calendar_Date");
    }
}

public sealed class SlaDefinitionConfiguration : IEntityTypeConfiguration<SlaDefinition>
{
    public void Configure(EntityTypeBuilder<SlaDefinition> builder)
    {
        builder.ToTable("SlaDefinitions", "sla");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1024);
        builder.Property(x => x.Module).HasConversion<int>();
        builder.Property(x => x.TargetType).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.BusinessCalendar).WithMany()
            .HasForeignKey(x => x.BusinessCalendarId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique()
            .HasDatabaseName("UX_SlaDefinitions_Tenant_Code");
    }
}

public sealed class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> builder)
    {
        builder.ToTable("SlaPolicies", "sla");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Module).HasConversion<int>();
        builder.Property(x => x.Priority).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.SlaDefinition).WithMany()
            .HasForeignKey(x => x.SlaDefinitionId).OnDelete(DeleteBehavior.Restrict);

        // Policy evaluation reads the ordered active list for a module on every incident change.
        builder.HasIndex(x => new { x.TenantId, x.Module, x.IsActive, x.Order })
            .HasDatabaseName("IX_SlaPolicies_Tenant_Module_Active_Order");
    }
}

public sealed class SlaInstanceConfiguration : IEntityTypeConfiguration<SlaInstance>
{
    public void Configure(EntityTypeBuilder<SlaInstance> builder)
    {
        builder.ToTable("SlaInstances", "sla");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SlaName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Module).HasConversion<int>();
        builder.Property(x => x.TargetType).HasConversion<int>();
        builder.Property(x => x.State).HasConversion<int>();
        builder.Property(x => x.PauseWhenPending).HasDefaultValue(true);

        // The calendar is a copied value, not a relationship: the commitment must not move if
        // the definition is later re-pointed at a different calendar.
        builder.Property(x => x.BusinessCalendarId);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.SlaDefinition).WithMany()
            .HasForeignKey(x => x.SlaDefinitionId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.Module, x.RecordId })
            .HasDatabaseName("IX_SlaInstances_Tenant_Module_Record");

        // The background monitor scans running clocks by due time across every tenant, so this
        // index deliberately leads with State rather than TenantId.
        builder.HasIndex(x => new { x.State, x.DueAt })
            .HasDatabaseName("IX_SlaInstances_State_DueAt");

        builder.HasIndex(x => new { x.Module, x.RecordId, x.TargetType })
            .HasDatabaseName("IX_SlaInstances_Record_Target");
    }
}

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents", "audit");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActorDisplayName).HasMaxLength(256);
        builder.Property(x => x.ActorEmail).HasMaxLength(256);
        builder.Property(x => x.EntityType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(64);
        builder.Property(x => x.EntityLabel).HasMaxLength(256);
        builder.Property(x => x.ChangedFields).HasMaxLength(2048);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasMaxLength(512);
        builder.Property(x => x.Message).HasMaxLength(2048);

        // Snapshots are JSON documents whose size is bounded by the entity, not by us.
        builder.Property(x => x.BeforeJson).HasMaxLength(32000);
        builder.Property(x => x.AfterJson).HasMaxLength(32000);

        builder.Property(x => x.Action).HasConversion<int>();
        builder.Property(x => x.Source).HasConversion<int>();
        builder.Property(x => x.Outcome).HasConversion<int>();

        builder.HasIndex(x => new { x.TenantId, x.OccurredAt }).HasDatabaseName("IX_AuditEvents_Tenant_Occurred");
        builder.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId })
            .HasDatabaseName("IX_AuditEvents_Tenant_Entity");
        builder.HasIndex(x => new { x.TenantId, x.ActorUserId, x.OccurredAt })
            .HasDatabaseName("IX_AuditEvents_Tenant_Actor");
        builder.HasIndex(x => x.CorrelationId).HasDatabaseName("IX_AuditEvents_Correlation");
    }
}

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments", "platform");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.FileName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.BlobContainer).HasMaxLength(64).IsRequired();
        builder.Property(x => x.BlobPath).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(64);
        builder.Property(x => x.Module).HasConversion<int>();
        builder.Property(x => x.ScanStatus).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.TenantId, x.Module, x.RecordId })
            .HasDatabaseName("IX_Attachments_Tenant_Module_Record");
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications", "platform");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ActionUrl).HasMaxLength(512);
        builder.Property(x => x.EmailFailureReason).HasMaxLength(1024);
        builder.Property(x => x.Kind).HasConversion<int>();
        builder.Property(x => x.Severity).HasConversion<int>();
        builder.Property(x => x.Module).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();

        // The bell icon polls unread counts for one user constantly; this index serves it.
        builder.HasIndex(x => new { x.TenantId, x.RecipientUserId, x.IsRead, x.CreatedAt })
            .HasDatabaseName("IX_Notifications_Recipient_Unread");

        // The email dispatcher scans for queued messages that have not been sent.
        builder.HasIndex(x => new { x.EmailRequested, x.EmailSentAt })
            .HasDatabaseName("IX_Notifications_EmailQueue");
    }
}

public sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
        builder.ToTable("NumberSequences", "platform");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Prefix).HasMaxLength(16).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.TenantId, x.Key }).IsUnique()
            .HasDatabaseName("UX_NumberSequences_Tenant_Key");
    }
}

public sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings", "platform");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(4000);
        builder.Property(x => x.Category).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(256);
        builder.Property(x => x.Description).HasMaxLength(1024);
        builder.Property(x => x.ValueType).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.TenantId, x.Key }).IsUnique()
            .HasDatabaseName("UX_SystemSettings_Tenant_Key");
    }
}
