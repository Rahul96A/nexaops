using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Knowledge;

/// <summary>
/// Article lifecycle. Legal transitions live in <see cref="KnowledgeStateMachine"/>.
/// </summary>
public enum ArticleStatus
{
    Draft = 1,

    /// <summary>Written and waiting on a reviewer.</summary>
    InReview = 2,

    Published = 3,

    /// <summary>
    /// Published but past its review date. Still readable - stale guidance beats none while
    /// somebody gets round to checking it - but flagged as unverified wherever it is shown.
    /// </summary>
    Stale = 4,

    /// <summary>Withdrawn. No longer surfaced in search, kept for the record.</summary>
    Retired = 5
}

/// <summary>Who an article is written for. Decides where it is surfaced.</summary>
public enum ArticleAudience
{
    /// <summary>Anyone in the tenant, including requesters. Self-service material.</summary>
    Everyone = 1,

    /// <summary>Service desk staff only. Internal runbooks and diagnostics.</summary>
    ServiceDesk = 2
}

/// <summary>
/// A knowledge article: how to do something, or what to do when something breaks.
/// <para>
/// The module earns its place through deflection - somebody reading an article instead of
/// raising a ticket - so the counters that matter are views and, more honestly, whether readers
/// found it useful. Both are recorded; neither is inferred.
/// </para>
/// </summary>
public class KnowledgeArticle : TenantEntity
{
    /// <summary>Human-facing identifier, e.g. KB0000042. Unique per tenant, never reused.</summary>
    public string Number { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>One line shown in search results, so a reader can judge before opening.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>The article itself, in Markdown.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Comma-free keywords a reader might search for but the body might not contain.</summary>
    public string? Keywords { get; set; }

    public Guid? CategoryId { get; set; }

    public ArticleStatus Status { get; set; } = ArticleStatus.Draft;
    public ArticleAudience Audience { get; set; } = ArticleAudience.ServiceDesk;

    // --- People ---
    public Guid AuthorId { get; set; }
    public Guid? ReviewerId { get; set; }
    public Guid? PublishedByUserId { get; set; }

    // --- Lifecycle ---
    public DateTimeOffset? SubmittedForReviewAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }

    /// <summary>
    /// When this should next be checked. An article nobody has verified in two years is a
    /// liability, so the date is set on publication rather than left optional.
    /// </summary>
    public DateTimeOffset? ReviewDueAt { get; set; }

    // --- Provenance ---

    /// <summary>The problem whose workaround this article documents.</summary>
    public Guid? ProblemId { get; set; }

    /// <summary>The incident that prompted writing it.</summary>
    public Guid? SourceIncidentId { get; set; }

    // --- Usage ---
    public int ViewCount { get; set; }
    public int HelpfulCount { get; set; }
    public int NotHelpfulCount { get; set; }

    /// <summary>Reason recorded when the article is retired, so the withdrawal is explainable.</summary>
    public string? RetirementReason { get; set; }

    // --- Navigation ---
    public User? Author { get; set; }
    public User? Reviewer { get; set; }
    public Category? Category { get; set; }

    /// <summary>
    /// The share of readers who said it helped, or null when nobody has said either way.
    /// <para>
    /// Null rather than zero on no feedback: an article nobody has rated is not an article
    /// everybody disliked, and showing 0% would quietly condemn every new article.
    /// </para>
    /// </summary>
    public double? HelpfulRatio
    {
        get
        {
            var total = HelpfulCount + NotHelpfulCount;
            return total == 0 ? null : (double)HelpfulCount / total;
        }
    }

    /// <summary>True when readers outside the service desk may see it.</summary>
    public bool IsPubliclyReadable =>
        Audience == ArticleAudience.Everyone
        && Status is ArticleStatus.Published or ArticleStatus.Stale;

    // ------------------------------------------------------------------
    // Behaviour
    // ------------------------------------------------------------------

    /// <summary>
    /// Moves the article to a new status, enforcing the state machine and the evidence each
    /// stage requires.
    /// </summary>
    /// <param name="reviewIntervalDays">
    /// How long a published article stays trusted before it needs re-checking.
    /// </param>
    public void TransitionTo(
        ArticleStatus next,
        Guid actorUserId,
        DateTimeOffset now,
        int reviewIntervalDays = 365)
    {
        KnowledgeStateMachine.EnsureCanTransition(Status, next);

        if (Status == next)
        {
            return;
        }

        if (next is ArticleStatus.InReview or ArticleStatus.Published)
        {
            if (string.IsNullOrWhiteSpace(Body))
            {
                throw new DomainException(
                    "knowledge.body_required",
                    "An article needs a body before it can be reviewed or published.");
            }

            if (string.IsNullOrWhiteSpace(Summary))
            {
                // The summary is what a searcher reads to decide whether to open it. Without one
                // the article is invisible in practice even though it exists.
                throw new DomainException(
                    "knowledge.summary_required",
                    "An article needs a one-line summary before it can be reviewed or published.");
            }
        }

        Status = next;

        switch (next)
        {
            case ArticleStatus.InReview:
                SubmittedForReviewAt ??= now;
                break;

            case ArticleStatus.Published:
                PublishedAt ??= now;
                PublishedByUserId = actorUserId;

                // Re-published after being stale: the clock restarts from now, not from the
                // original publication.
                ReviewDueAt = now.AddDays(reviewIntervalDays);
                break;

            case ArticleStatus.Retired:
                RetiredAt = now;
                break;
        }
    }

    /// <summary>
    /// Marks a published article as needing re-verification. Called by the background sweep, not
    /// by a person.
    /// </summary>
    public bool MarkStaleIfOverdue(DateTimeOffset now)
    {
        if (Status != ArticleStatus.Published || ReviewDueAt is null || now <= ReviewDueAt)
        {
            return false;
        }

        Status = ArticleStatus.Stale;
        return true;
    }

    /// <summary>Withdraws the article, recording why.</summary>
    public void Retire(string? reason, Guid actorUserId, DateTimeOffset now)
    {
        KnowledgeStateMachine.EnsureCanTransition(Status, ArticleStatus.Retired);

        RetirementReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        TransitionTo(ArticleStatus.Retired, actorUserId, now);
    }

    /// <summary>Records one reader's verdict.</summary>
    public void RecordFeedback(bool wasHelpful)
    {
        if (wasHelpful)
        {
            HelpfulCount++;
        }
        else
        {
            NotHelpfulCount++;
        }
    }

    /// <summary>Records a read. Deliberately separate from feedback, which is opt-in.</summary>
    public void RecordView() => ViewCount++;
}

/// <summary>
/// One reader's feedback on an article.
/// <para>
/// Stored per reader rather than only as a counter, so a person can change their mind without
/// double-counting and so "who found this unhelpful and why" is answerable.
/// </para>
/// </summary>
public class ArticleFeedback : TenantEntity
{
    public Guid ArticleId { get; set; }

    public Guid UserId { get; set; }

    public bool WasHelpful { get; set; }

    /// <summary>Optional note. The most valuable field in the module when it is filled in.</summary>
    public string? Comment { get; set; }

    public KnowledgeArticle? Article { get; set; }
    public User? User { get; set; }
}
