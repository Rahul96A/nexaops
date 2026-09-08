using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaOps.Domain.Knowledge;

namespace NexaOps.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeArticleConfiguration : IEntityTypeConfiguration<KnowledgeArticle>
{
    public void Configure(EntityTypeBuilder<KnowledgeArticle> builder)
    {
        builder.ToTable("Articles", "knowledge");

        builder.Property(x => x.Number).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(600);
        builder.Property(x => x.Keywords).HasMaxLength(1000);
        builder.Property(x => x.RetirementReason).HasMaxLength(1000);

        // The one genuinely unbounded column in the schema. An article is the product here, and
        // capping it at a few thousand characters would make the module useless for runbooks.
        builder.Property(x => x.Body).HasColumnType("nvarchar(max)").IsRequired();

        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.Audience).HasConversion<int>();

        builder.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);
        builder.HasOne(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewerId);
        builder.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);

        builder.HasIndex(x => new { x.TenantId, x.Number })
            .IsUnique()
            .HasDatabaseName("UX_Articles_Tenant_Number");

        // Audience sits beside status because every read path filters on both: a requester sees
        // published, everyone-audience articles and nothing else.
        builder.HasIndex(x => new { x.TenantId, x.Status, x.Audience })
            .HasDatabaseName("IX_Articles_Tenant_Status_Audience");

        builder.HasIndex(x => new { x.TenantId, x.CategoryId, x.Status })
            .HasDatabaseName("IX_Articles_Tenant_Category_Status");

        // Serves the background sweep that marks overdue articles stale.
        builder.HasIndex(x => new { x.Status, x.ReviewDueAt })
            .HasDatabaseName("IX_Articles_Status_ReviewDue");

        builder.HasIndex(x => new { x.TenantId, x.ProblemId })
            .HasDatabaseName("IX_Articles_Tenant_Problem");
    }
}

public sealed class ArticleFeedbackConfiguration : IEntityTypeConfiguration<ArticleFeedback>
{
    public void Configure(EntityTypeBuilder<ArticleFeedback> builder)
    {
        builder.ToTable("ArticleFeedback", "knowledge");

        builder.Property(x => x.Comment).HasMaxLength(2000);

        builder.HasOne(x => x.Article)
            .WithMany()
            .HasForeignKey(x => x.ArticleId);

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);

        // One verdict per reader per article, so changing your mind updates rather than
        // double-counts.
        builder.HasIndex(x => new { x.ArticleId, x.UserId })
            .IsUnique()
            .HasDatabaseName("UX_ArticleFeedback_Article_User");
    }
}
