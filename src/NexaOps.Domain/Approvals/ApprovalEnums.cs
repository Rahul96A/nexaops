namespace NexaOps.Domain.Approvals;

/// <summary>
/// State of a single approval record.
/// <para>
/// <see cref="NotRequired"/> exists as a distinct state rather than simply omitting the record,
/// because "nobody had to approve this" and "this was never routed for approval" are different
/// facts and an auditor will ask which one applied.
/// </para>
/// </summary>
public enum ApprovalState
{
    /// <summary>Waiting on this approver.</summary>
    Pending = 1,

    Approved = 2,
    Rejected = 3,

    /// <summary>The request was cancelled or withdrawn before this approver acted.</summary>
    Cancelled = 4,

    /// <summary>Superseded - another approver in the same parallel stage already decided.</summary>
    NotRequired = 5
}

/// <summary>
/// How the approvers in one stage combine.
/// </summary>
public enum ApprovalRule
{
    /// <summary>Every approver must approve. Any rejection fails the stage.</summary>
    Unanimous = 1,

    /// <summary>The first decision settles the stage, whichever way it goes.</summary>
    AnyOne = 2
}

/// <summary>Who an approval is addressed to.</summary>
public enum ApprovalTargetKind
{
    /// <summary>A named individual.</summary>
    User = 1,

    /// <summary>Any member of a group may act. Resolved to a group, not expanded to members.</summary>
    Group = 2,

    /// <summary>The requester's line manager, resolved when the approval is raised.</summary>
    Manager = 3
}
