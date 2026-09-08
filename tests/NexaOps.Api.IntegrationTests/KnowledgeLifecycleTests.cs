using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Common;
using NexaOps.Application.Knowledge;
using NexaOps.Domain.Knowledge;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// The knowledge base end to end, over real HTTP against real SQL Server.
/// <para>
/// The behaviours worth protecting are that a requester cannot discover internal runbooks, that
/// an author cannot self-publish, and that feedback moves rather than double-counts when a
/// reader changes their mind.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class KnowledgeLifecycleTests
{
    private readonly TestEnvironment _env;

    public KnowledgeLifecycleTests(TestEnvironment env) => _env = env;

    [Fact]
    public async Task An_article_cannot_be_published_without_a_summary_or_body()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var article = await DraftAsync(manager, "Empty article", summary: null, body: null);

        var response = await manager.PostAsJsonAsync($"/api/v1/knowledge/{article.Id}/status",
            new ChangeArticleStatusCommand { Status = ArticleStatus.Published });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBeOneOf("knowledge.body_required", "knowledge.summary_required");
    }

    [Fact]
    public async Task An_author_cannot_publish_their_own_article()
    {
        // Publishing puts an article in front of the whole organisation, so it is deliberately
        // separate from writing one.
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var article = await DraftAsync(agent, "Agent-written runbook");

        var response = await agent.PostAsJsonAsync($"/api/v1/knowledge/{article.Id}/status",
            new ChangeArticleStatusCommand { Status = ArticleStatus.Published });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Publishing_sets_a_review_date_rather_than_leaving_it_open_ended()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var article = await DraftAsync(manager, "Resetting a VPN password");

        var response = await manager.PostAsJsonAsync($"/api/v1/knowledge/{article.Id}/status",
            new ChangeArticleStatusCommand
            {
                Status = ArticleStatus.Published,
                ReviewIntervalDays = 90
            });

        response.EnsureSuccessStatusCode();

        var published = await response.Content.ReadFromJsonAsync<ArticleDetailDto>(TestEnvironment.Json);
        published!.Status.ShouldBe(ArticleStatus.Published);
        published.PublishedAt.ShouldNotBeNull();
        published.ReviewDueAt.ShouldNotBeNull();

        // An article nobody has verified in two years is a liability.
        published.ReviewDueAt!.Value.ShouldBeGreaterThan(published.PublishedAt!.Value);
    }

    [Fact]
    public async Task A_requester_cannot_see_that_an_internal_runbook_exists()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var internalArticle = await DraftAsync(
            manager, "Internal: restarting the payment gateway", audience: ArticleAudience.ServiceDesk);

        await manager.PostAsJsonAsync($"/api/v1/knowledge/{internalArticle.Id}/status",
            new ChangeArticleStatusCommand { Status = ArticleStatus.Published });

        // Invisible rather than merely unopenable.
        (await employee.GetAsync($"/api/v1/knowledge/{internalArticle.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var search = await employee.GetFromJsonAsync<PagedResult<ArticleSummaryDto>>(
            $"/api/v1/knowledge?search={internalArticle.Number}", TestEnvironment.Json);

        search!.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_requester_can_read_a_published_article_written_for_everyone()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var article = await DraftAsync(
            manager, "How to connect to the guest Wi-Fi", audience: ArticleAudience.Everyone);

        await manager.PostAsJsonAsync($"/api/v1/knowledge/{article.Id}/status",
            new ChangeArticleStatusCommand { Status = ArticleStatus.Published });

        var read = await employee.GetFromJsonAsync<ArticleDetailDto>(
            $"/api/v1/knowledge/{article.Id}", TestEnvironment.Json);

        read!.Title.ShouldBe("How to connect to the guest Wi-Fi");
    }

    [Fact]
    public async Task A_draft_is_never_visible_to_a_requester_even_when_written_for_everyone()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var draft = await DraftAsync(manager, "Unfinished guidance", audience: ArticleAudience.Everyone);

        (await employee.GetAsync($"/api/v1/knowledge/{draft.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reading_an_article_records_the_view()
    {
        // Deflection reporting is the module's justification, so the count has to be real.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var article = await PublishAsync(manager, "Counted views");

        await manager.GetAsync($"/api/v1/knowledge/{article.Id}");
        await manager.GetAsync($"/api/v1/knowledge/{article.Id}");

        var reread = await manager.GetFromJsonAsync<ArticleDetailDto>(
            $"/api/v1/knowledge/{article.Id}", TestEnvironment.Json);

        reread!.ViewCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task An_unrated_article_has_no_rating_rather_than_a_bad_one()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var article = await PublishAsync(manager, "Never rated");

        var detail = await manager.GetFromJsonAsync<ArticleDetailDto>(
            $"/api/v1/knowledge/{article.Id}", TestEnvironment.Json);

        // Showing 0% would quietly condemn every new article.
        detail!.HelpfulRatio.ShouldBeNull();
    }

    [Fact]
    public async Task Changing_your_mind_moves_the_vote_rather_than_double_counting()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var article = await PublishAsync(manager, "Rated then re-rated", audience: ArticleAudience.Everyone);

        var first = await employee.PostAsJsonAsync($"/api/v1/knowledge/{article.Id}/feedback",
            new ArticleFeedbackCommand { WasHelpful = true });

        first.EnsureSuccessStatusCode();

        var afterFirst = await first.Content.ReadFromJsonAsync<ArticleDetailDto>(TestEnvironment.Json);
        afterFirst!.HelpfulCount.ShouldBe(1);
        afterFirst.NotHelpfulCount.ShouldBe(0);
        afterFirst.MyFeedback.ShouldBe(true);

        var second = await employee.PostAsJsonAsync($"/api/v1/knowledge/{article.Id}/feedback",
            new ArticleFeedbackCommand { WasHelpful = false, Comment = "Out of date." });

        second.EnsureSuccessStatusCode();

        var afterSecond = await second.Content.ReadFromJsonAsync<ArticleDetailDto>(TestEnvironment.Json);

        // One person, one vote, moved.
        afterSecond!.HelpfulCount.ShouldBe(0);
        afterSecond.NotHelpfulCount.ShouldBe(1);
        afterSecond.MyFeedback.ShouldBe(false);
    }

    [Fact]
    public async Task An_unpublished_article_cannot_be_rated()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var draft = await DraftAsync(manager, "Not yet published");

        var response = await manager.PostAsJsonAsync($"/api/v1/knowledge/{draft.Id}/feedback",
            new ArticleFeedbackCommand { WasHelpful = true });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("knowledge.not_published");
    }

    [Fact]
    public async Task A_retired_article_can_be_reinstated_rather_than_copied()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var article = await PublishAsync(manager, "Withdrawn then reinstated");

        var retired = await manager.PostAsJsonAsync($"/api/v1/knowledge/{article.Id}/status",
            new ChangeArticleStatusCommand
            {
                Status = ArticleStatus.Retired,
                Reason = "Superseded by the new starter guide."
            });

        retired.EnsureSuccessStatusCode();

        var withdrawn = await retired.Content.ReadFromJsonAsync<ArticleDetailDto>(TestEnvironment.Json);
        withdrawn!.Status.ShouldBe(ArticleStatus.Retired);
        withdrawn.RetirementReason.ShouldBe("Superseded by the new starter guide.");

        // Forcing a copy would lose the article's history and its usage counters.
        withdrawn.AllowedTransitions.ShouldContain(ArticleStatus.Draft);
    }

    [Fact]
    public async Task Another_tenants_article_is_not_reachable()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var neighbour = await _env.ClientForAsync(_env.Northwind.Manager);

        var article = await PublishAsync(manager, "Acme-only article", audience: ArticleAudience.Everyone);

        (await neighbour.GetAsync($"/api/v1/knowledge/{article.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var search = await neighbour.GetFromJsonAsync<PagedResult<ArticleSummaryDto>>(
            $"/api/v1/knowledge?search={article.Number}", TestEnvironment.Json);

        search!.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_unrecognised_sort_field_is_a_validation_failure()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await manager.GetAsync("/api/v1/knowledge?sortBy=; DROP TABLE Articles");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static async Task<ArticleDetailDto> DraftAsync(
        HttpClient client,
        string title,
        string? summary = "A one-line summary for the search results.",
        string? body = "1. Do the first thing.\n2. Do the second thing.",
        ArticleAudience audience = ArticleAudience.ServiceDesk)
    {
        var response = await client.PostAsJsonAsync("/api/v1/knowledge", new CreateArticleCommand
        {
            Title = title,
            Summary = summary,
            Body = body,
            Audience = audience,
            Keywords = "integration suite"
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ArticleDetailDto>(TestEnvironment.Json))!;
    }

    private static async Task<ArticleDetailDto> PublishAsync(
        HttpClient client,
        string title,
        ArticleAudience audience = ArticleAudience.ServiceDesk)
    {
        var draft = await DraftAsync(client, title, audience: audience);

        var response = await client.PostAsJsonAsync($"/api/v1/knowledge/{draft.Id}/status",
            new ChangeArticleStatusCommand { Status = ArticleStatus.Published });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ArticleDetailDto>(TestEnvironment.Json))!;
    }
}
