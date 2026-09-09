using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaOps.Domain.Integration;

namespace NexaOps.Infrastructure.Persistence.Configurations;

public sealed class IntegrationKeyConfiguration : IEntityTypeConfiguration<IntegrationKey>
{
    public void Configure(EntityTypeBuilder<IntegrationKey> builder)
    {
        builder.ToTable("IntegrationKeys", "integration");

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Prefix).HasMaxLength(16).IsRequired();

        // 64 hex characters of SHA-256, fixed width.
        builder.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();

        builder.Property(x => x.Scope).HasConversion<int>();

        builder.HasOne(x => x.ServiceAccount).WithMany().HasForeignKey(x => x.ServiceAccountUserId);

        // The authentication lookup, and the only index that matters for latency: it runs on
        // every call an integration makes. Unique across tenants because the hash is of 256 bits
        // of randomness — a collision would mean a broken generator, and failing loudly on the
        // insert is the right way to find that out.
        builder.HasIndex(x => x.KeyHash)
            .IsUnique()
            .HasDatabaseName("UX_IntegrationKeys_KeyHash");

        builder.HasIndex(x => new { x.TenantId, x.Name })
            .HasDatabaseName("IX_IntegrationKeys_Tenant_Name");
    }
}

public sealed class InboundMessageConfiguration : IEntityTypeConfiguration<InboundMessage>
{
    public void Configure(EntityTypeBuilder<InboundMessage> builder)
    {
        builder.ToTable("InboundMessages", "integration");

        // Message-IDs are bounded by RFC 5322 in practice but not in law; clients emit long
        // ones. Wide enough for anything real, narrow enough to index.
        builder.Property(x => x.ExternalMessageId).HasMaxLength(512).IsRequired();
        builder.Property(x => x.InReplyTo).HasMaxLength(512);
        builder.Property(x => x.FromAddress).HasMaxLength(320).IsRequired();
        builder.Property(x => x.FromDisplayName).HasMaxLength(200);
        builder.Property(x => x.Subject).HasMaxLength(500).IsRequired();
        builder.Property(x => x.BodyPreview).HasMaxLength(500);
        builder.Property(x => x.RecordNumber).HasMaxLength(32);
        builder.Property(x => x.Outcome).HasMaxLength(1000);

        builder.Property(x => x.Status).HasConversion<int>();

        // The idempotency guarantee, enforced by the database rather than by the check in the
        // service. Two deliveries of the same message racing each other would both pass that
        // check; only one can win this index, and the loser fails rather than duplicating a
        // ticket.
        builder.HasIndex(x => new { x.TenantId, x.ExternalMessageId })
            .IsUnique()
            .HasDatabaseName("UX_InboundMessages_Tenant_MessageId");

        // Threading looks messages up by what they are a reply to.
        builder.HasIndex(x => new { x.TenantId, x.InReplyTo })
            .HasDatabaseName("IX_InboundMessages_Tenant_InReplyTo");

        builder.HasIndex(x => new { x.TenantId, x.ReceivedAt })
            .HasDatabaseName("IX_InboundMessages_Tenant_Received");

        // No foreign key to the record. A message that failed points at nothing, and an
        // archived incident should not stop the evidence of what arrived from being readable.
        builder.HasIndex(x => new { x.TenantId, x.RecordId })
            .HasDatabaseName("IX_InboundMessages_Tenant_Record");
    }
}
