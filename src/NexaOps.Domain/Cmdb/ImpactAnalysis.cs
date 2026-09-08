namespace NexaOps.Domain.Cmdb;

/// <summary>
/// One configuration item reached while walking the dependency graph.
/// </summary>
/// <param name="ItemId">The item that is affected.</param>
/// <param name="Depth">
/// How many hops away it is. Depth 1 is directly dependent; deeper means the effect is
/// transitive, which matters when deciding who to warn first.
/// </param>
/// <param name="ViaRelationship">The edge type that brought us here.</param>
public sealed record ImpactedItem(Guid ItemId, int Depth, CiRelationshipType ViaRelationship);

/// <summary>
/// Walks the dependency graph to answer the question an outage forces: if this breaks, what else
/// stops working?
/// <para>
/// Pure graph arithmetic over edges, with no database and no notion of who is asking, so it is
/// directly testable and behaves identically whether it is called from a change's impact
/// assessment, an incident's blast radius, or a CI record page.
/// </para>
/// </summary>
public static class ImpactAnalysis
{
    /// <summary>
    /// A hard ceiling on how far the walk goes.
    /// <para>
    /// A real CMDB accumulates long chains, and a walk that follows every one of them turns a
    /// record page into a timeout. Six hops is well past the point where the answer is still
    /// actionable for a human.
    /// </para>
    /// </summary>
    public const int MaxDepth = 6;

    /// <summary>
    /// Everything that depends on <paramref name="rootId"/>, directly or transitively.
    /// </summary>
    /// <param name="rootId">The item that is failing.</param>
    /// <param name="edges">
    /// Every relationship in scope, in the direction they are stored: source depends on target.
    /// </param>
    /// <param name="maxDepth">How far to walk. Clamped to <see cref="MaxDepth"/>.</param>
    public static IReadOnlyList<ImpactedItem> WhatBreaksIf(
        Guid rootId,
        IReadOnlyCollection<CiRelationship> edges,
        int maxDepth = MaxDepth)
    {
        ArgumentNullException.ThrowIfNull(edges);

        // Dependants are found by looking at the target end: if A depends on B, then breaking B
        // affects A. Walking the source end instead would answer the opposite question.
        var byTarget = edges
            .GroupBy(e => e.TargetId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return Walk(rootId, byTarget, e => e.SourceId, maxDepth);
    }

    /// <summary>
    /// Everything <paramref name="rootId"/> relies on, directly or transitively. The question
    /// asked when planning a change rather than when handling an outage.
    /// </summary>
    public static IReadOnlyList<ImpactedItem> WhatItDependsOn(
        Guid rootId,
        IReadOnlyCollection<CiRelationship> edges,
        int maxDepth = MaxDepth)
    {
        ArgumentNullException.ThrowIfNull(edges);

        var bySource = edges
            .GroupBy(e => e.SourceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return Walk(rootId, bySource, e => e.TargetId, maxDepth);
    }

    /// <summary>
    /// Breadth-first so that the shallowest path to each item wins.
    /// <para>
    /// That matters: an item reachable at both depth 1 and depth 4 is directly affected, and
    /// reporting it as four hops away would understate the urgency. Visited tracking also makes
    /// the walk safe on a cyclic graph, which real CMDBs contain - two servers that each fail
    /// over to the other is a cycle, and a correct one.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ImpactedItem> Walk(
        Guid rootId,
        Dictionary<Guid, List<CiRelationship>> index,
        Func<CiRelationship, Guid> next,
        int maxDepth)
    {
        var limit = Math.Clamp(maxDepth, 1, MaxDepth);

        var results = new List<ImpactedItem>();
        var visited = new HashSet<Guid> { rootId };
        var frontier = new Queue<(Guid Id, int Depth)>();

        frontier.Enqueue((rootId, 0));

        while (frontier.Count > 0)
        {
            var (currentId, depth) = frontier.Dequeue();

            if (depth >= limit || !index.TryGetValue(currentId, out var edges))
            {
                continue;
            }

            foreach (var edge in edges)
            {
                var neighbourId = next(edge);

                if (!visited.Add(neighbourId))
                {
                    continue;
                }

                results.Add(new ImpactedItem(neighbourId, depth + 1, edge.Type));
                frontier.Enqueue((neighbourId, depth + 1));
            }
        }

        return results;
    }
}
