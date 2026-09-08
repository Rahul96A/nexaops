namespace NexaOps.Domain.Changes;

/// <summary>
/// How much scrutiny a change needs before it may proceed.
/// <para>
/// The distinction is the entire point of change management. Treating every change as if it
/// needed a committee is how organisations end up with an unofficial process that bypasses the
/// official one.
/// </para>
/// </summary>
public enum ChangeType
{
    /// <summary>
    /// Pre-authorised and low risk: a routine, repeatedly-proven procedure. Needs no approval,
    /// because the approval was granted once when the procedure was accepted.
    /// </summary>
    Standard = 1,

    /// <summary>The default. Assessed and approved before it is scheduled.</summary>
    Normal = 2,

    /// <summary>
    /// Restores or protects service now. Approval is recorded retrospectively rather than
    /// waived - an emergency change that nobody ever reviewed is an outage waiting to repeat.
    /// </summary>
    Emergency = 3
}

/// <summary>
/// Change lifecycle. Legal transitions live in <see cref="ChangeStateMachine"/>.
/// </summary>
public enum ChangeStatus
{
    Draft = 1,

    /// <summary>Being assessed for risk, impact and a plan.</summary>
    Assessing = 2,

    /// <summary>Waiting on the change advisory board or a named approver.</summary>
    AwaitingApproval = 3,

    /// <summary>Approved and booked into a window.</summary>
    Scheduled = 4,

    /// <summary>The window is open and the work is under way.</summary>
    Implementing = 5,

    /// <summary>
    /// Implemented and awaiting a post-implementation review. A change is not finished when the
    /// work stops; it is finished when somebody has confirmed it did what it claimed.
    /// </summary>
    Review = 6,

    Closed = 7,

    /// <summary>An approver refused. Terminal - a new change is raised rather than re-submitted.</summary>
    Rejected = 8,

    Cancelled = 9
}

/// <summary>
/// How the change went. Recorded at review, and the reason the review stage exists.
/// </summary>
public enum ChangeOutcome
{
    Successful = 1,

    /// <summary>Delivered, but with unplanned side effects or an overrun.</summary>
    SuccessfulWithIssues = 2,

    Failed = 3,

    /// <summary>Backed out during the window.</summary>
    RolledBack = 4
}

/// <summary>
/// How much damage a failure would do. Combined with likelihood to decide who must approve.
/// </summary>
public enum ChangeRisk
{
    Low = 1,
    Medium = 2,
    High = 3,

    /// <summary>Service-affecting for the whole organisation if it goes wrong.</summary>
    VeryHigh = 4
}
