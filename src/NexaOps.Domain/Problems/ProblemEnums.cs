namespace NexaOps.Domain.Problems;

/// <summary>
/// Problem lifecycle. Legal transitions live in <see cref="ProblemStateMachine"/>.
/// <para>
/// A problem is the underlying cause of one or more incidents. Its lifecycle is investigative
/// rather than restorative: the goal is to understand why, publish a workaround so the service
/// desk can stop firefighting, and then remove the cause for good.
/// </para>
/// </summary>
public enum ProblemStatus
{
    New = 1,

    /// <summary>Root cause is being investigated.</summary>
    Investigating = 2,

    /// <summary>
    /// Cause understood and a workaround published. This is a genuine, valuable end state on its
    /// own - most problems live here for a long time, and the service desk benefits immediately.
    /// </summary>
    KnownError = 3,

    /// <summary>A permanent fix has been identified and is being delivered, usually via a change.</summary>
    FixInProgress = 4,

    /// <summary>The cause has been removed.</summary>
    Resolved = 5,

    Closed = 6,

    /// <summary>Investigated and closed without a cause being found or worth pursuing.</summary>
    Cancelled = 7
}

/// <summary>How the problem came to be raised. Useful for measuring proactive practice.</summary>
public enum ProblemOrigin
{
    /// <summary>Raised from a recurring or major incident. The common case.</summary>
    FromIncident = 1,

    /// <summary>Found by analysing trends before anyone complained.</summary>
    Proactive = 2,

    /// <summary>Flagged by a supplier or vendor advisory.</summary>
    Vendor = 3,

    /// <summary>Identified during a post-incident review.</summary>
    PostIncidentReview = 4
}

/// <summary>
/// How confident the team is in the stated root cause.
/// <para>
/// Recorded explicitly because "we think it is the database" and "we proved it is the database"
/// justify very different amounts of spend on the permanent fix.
/// </para>
/// </summary>
public enum RootCauseConfidence
{
    Suspected = 1,
    Probable = 2,
    Confirmed = 3
}
