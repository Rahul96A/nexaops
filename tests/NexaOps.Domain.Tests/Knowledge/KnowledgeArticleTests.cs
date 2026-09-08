using NexaOps.Domain.Common;
using NexaOps.Domain.Knowledge;

namespace NexaOps.Domain.Tests.Knowledge;

/// <summary>
/// The rules governing knowledge articles.
/// <para>
/// The two that shape the module: a stale article stays readable, because old guidance beats
/// none; and an unrated article has no rating rather than a bad one.
/// </para>
/// </summary>
public sealed class KnowledgeStateMachineTests
{
    [Theory]
    [InlineData(ArticleStatus.Draft, ArticleStatus.InReview)]
    [InlineData(ArticleStatus.Draft, ArticleStatus.Published)]
    [InlineData(ArticleStatus.InReview, ArticleStatus.Published)]
    [InlineData(ArticleStatus.InReview, ArticleStatus.Draft)]
    [InlineData(ArticleStatus.Published, ArticleStatus.Stale)]
    [InlineData(ArticleStatus.Stale, ArticleStatus.Published)]
    [InlineData(ArticleStatus.Published, ArticleStatus.Retired)]
    public void Legal_transitions_are_permitted(ArticleStatus from, ArticleStatus to)
        => KnowledgeStateMachine.CanTransition(from, to).ShouldBeTrue();

    [Fact]
    public void A_retired_article_can_be_reinstated_rather_than_copied()
    {
        // Forcing a copy would lose the article's history and its usage counters.
        KnowledgeStateMachine.CanTransition(ArticleStatus.Retired, ArticleStatus.Draft)
            .ShouldBeTrue();

        KnowledgeStateMachine.AllowedTransitionsFrom(ArticleStatus.Retired)
            .ShouldBe([ArticleStatus.Draft]);
    }

    [Fact]
    public void A_stale_article_is_still_readable()
    {
        // Withdrawing guidance the moment its review date passes leaves the service desk with
        // nothing, which is worse than guidance that is merely old.
        KnowledgeStateMachine.IsReadable(ArticleStatus.Stale).ShouldBeTrue();
        KnowledgeStateMachine.IsReadable(ArticleStatus.Published).ShouldBeTrue();

        KnowledgeStateMachine.IsReadable(ArticleStatus.Draft).ShouldBeFalse();
        KnowledgeStateMachine.IsReadable(ArticleStatus.Retired).ShouldBeFalse();
    }

    [Fact]
    public void An_illegal_transition_names_the_move_that_was_refused()
    {
        var error = Should.Throw<DomainException>(
            () => KnowledgeStateMachine.EnsureCanTransition(ArticleStatus.Retired, ArticleStatus.Published));

        error.Code.ShouldBe("knowledge.invalid_transition");
        error.Message.ShouldContain("Retired");
    }
}

/// <summary>What an article requires before it can be published, and how usage is counted.</summary>
public sealed class KnowledgeArticleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 11, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid();

    private static KnowledgeArticle Article() => new()
    {
        TenantId = Guid.NewGuid(),
        Number = "KB0000042",
        Title = "Resetting a forgotten VPN password",
        Summary = "How to reset your VPN password from the self-service portal.",
        Body = "1. Open the portal.\n2. Choose Reset VPN password.",
        AuthorId = Guid.NewGuid(),
        Status = ArticleStatus.Draft
    };

    [Fact]
    public void An_article_cannot_be_published_without_a_body()
    {
        var article = Article();
        article.Body = "";

        Should.Throw<DomainException>(() => article.TransitionTo(ArticleStatus.Published, Actor, Now))
            .Code.ShouldBe("knowledge.body_required");
    }

    [Fact]
    public void An_article_cannot_be_published_without_a_summary()
    {
        // The summary is what a searcher reads to decide whether to open it. Without one the
        // article is invisible in practice even though it exists.
        var article = Article();
        article.Summary = "  ";

        Should.Throw<DomainException>(() => article.TransitionTo(ArticleStatus.Published, Actor, Now))
            .Code.ShouldBe("knowledge.summary_required");
    }

    [Fact]
    public void Publishing_sets_a_review_date_rather_than_leaving_it_open_ended()
    {
        // An article nobody has verified in two years is a liability, so the date is set on
        // publication rather than left optional.
        var article = Article();

        article.TransitionTo(ArticleStatus.Published, Actor, Now, reviewIntervalDays: 180);

        article.PublishedAt.ShouldBe(Now);
        article.PublishedByUserId.ShouldBe(Actor);
        article.ReviewDueAt.ShouldBe(Now.AddDays(180));
    }

    [Fact]
    public void A_published_article_goes_stale_once_its_review_date_passes()
    {
        var article = Article();
        article.TransitionTo(ArticleStatus.Published, Actor, Now, reviewIntervalDays: 30);

        article.MarkStaleIfOverdue(Now.AddDays(29)).ShouldBeFalse();
        article.Status.ShouldBe(ArticleStatus.Published);

        article.MarkStaleIfOverdue(Now.AddDays(31)).ShouldBeTrue();
        article.Status.ShouldBe(ArticleStatus.Stale);
    }

    [Fact]
    public void Only_a_published_article_can_go_stale()
    {
        var article = Article();

        article.MarkStaleIfOverdue(Now.AddYears(5)).ShouldBeFalse();
        article.Status.ShouldBe(ArticleStatus.Draft);
    }

    [Fact]
    public void Republishing_a_stale_article_restarts_its_review_clock()
    {
        var article = Article();
        article.TransitionTo(ArticleStatus.Published, Actor, Now, reviewIntervalDays: 30);
        article.MarkStaleIfOverdue(Now.AddDays(31));

        var reverified = Now.AddDays(40);
        article.TransitionTo(ArticleStatus.Published, Actor, reverified, reviewIntervalDays: 30);

        // From now, not from the original publication.
        article.ReviewDueAt.ShouldBe(reverified.AddDays(30));
        article.PublishedAt.ShouldBe(Now);
    }

    [Fact]
    public void An_unrated_article_has_no_rating_rather_than_a_bad_one()
    {
        // Showing 0% would quietly condemn every new article.
        var article = Article();

        article.HelpfulRatio.ShouldBeNull();

        article.RecordFeedback(wasHelpful: true);
        article.HelpfulRatio.ShouldBe(1.0);

        article.RecordFeedback(wasHelpful: false);
        article.HelpfulRatio!.Value.ShouldBe(0.5, 0.001);
    }

    [Fact]
    public void Views_and_feedback_are_counted_separately()
    {
        var article = Article();

        article.RecordView();
        article.RecordView();

        article.ViewCount.ShouldBe(2);
        article.HelpfulCount.ShouldBe(0);
        article.NotHelpfulCount.ShouldBe(0);
    }

    [Fact]
    public void Only_a_published_article_written_for_everyone_is_publicly_readable()
    {
        var article = Article();
        article.Audience = ArticleAudience.ServiceDesk;
        article.TransitionTo(ArticleStatus.Published, Actor, Now);

        article.IsPubliclyReadable.ShouldBeFalse();

        article.Audience = ArticleAudience.Everyone;
        article.IsPubliclyReadable.ShouldBeTrue();

        // Still readable when stale: old guidance beats none.
        article.MarkStaleIfOverdue(Now.AddYears(2));
        article.IsPubliclyReadable.ShouldBeTrue();
    }

    [Fact]
    public void Retiring_records_why_and_when()
    {
        var article = Article();
        article.TransitionTo(ArticleStatus.Published, Actor, Now);

        article.Retire("Superseded by the new starter guide.", Actor, Now.AddDays(10));

        article.Status.ShouldBe(ArticleStatus.Retired);
        article.RetirementReason.ShouldBe("Superseded by the new starter guide.");
        article.RetiredAt.ShouldBe(Now.AddDays(10));
        article.IsPubliclyReadable.ShouldBeFalse();
    }
}
