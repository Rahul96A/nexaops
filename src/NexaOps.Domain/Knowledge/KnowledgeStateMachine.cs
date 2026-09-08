using NexaOps.Domain.Common;

namespace NexaOps.Domain.Knowledge;

/// <summary>
/// The single source of truth for which knowledge article transitions are legal.
/// <para>
/// The rule that distinguishes this module is that <see cref="ArticleStatus.Stale"/> is reached
/// by the passage of time rather than by anybody's decision, and that a stale article is still
/// readable. Withdrawing guidance the moment its review date passes would leave the service desk
/// with nothing, which is worse than guidance that is merely old.
/// </para>
/// </summary>
public static class KnowledgeStateMachine
{
    private static readonly Dictionary<ArticleStatus, ArticleStatus[]> Allowed = new()
    {
        [ArticleStatus.Draft] = [ArticleStatus.InReview, ArticleStatus.Published, ArticleStatus.Retired],

        [ArticleStatus.InReview] = [ArticleStatus.Published, ArticleStatus.Draft, ArticleStatus.Retired],

        // Published articles go stale on their own; an editor can also pull one back to draft to
        // rework it, which unpublishes it.
        [ArticleStatus.Published] = [ArticleStatus.Stale, ArticleStatus.Draft, ArticleStatus.Retired],

        // Re-verifying a stale article republishes it and restarts its review clock.
        [ArticleStatus.Stale] = [ArticleStatus.Published, ArticleStatus.Draft, ArticleStatus.Retired],

        // Retired is not terminal: withdrawn guidance is sometimes worth reinstating, and
        // forcing a copy would lose the article's history and its usage counters.
        [ArticleStatus.Retired] = [ArticleStatus.Draft]
    };

    public static bool CanTransition(ArticleStatus from, ArticleStatus to)
        => from == to || (Allowed.TryGetValue(from, out var targets) && targets.Contains(to));

    public static IReadOnlyCollection<ArticleStatus> AllowedTransitionsFrom(ArticleStatus from)
        => Allowed.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>Throws <see cref="DomainException"/> when the transition is not permitted.</summary>
    public static void EnsureCanTransition(ArticleStatus from, ArticleStatus to)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        throw new DomainException(
            "knowledge.invalid_transition",
            $"An article cannot move from {from} to {to}.");
    }

    /// <summary>
    /// True when the article is surfaced in search.
    /// <para>
    /// Stale counts as readable: old guidance beats none while somebody gets round to checking
    /// it. The UI flags it as unverified rather than hiding it.
    /// </para>
    /// </summary>
    public static bool IsReadable(ArticleStatus status)
        => status is ArticleStatus.Published or ArticleStatus.Stale;

    /// <summary>True while the article is still being written or checked.</summary>
    public static bool IsInProgress(ArticleStatus status)
        => status is ArticleStatus.Draft or ArticleStatus.InReview;
}
