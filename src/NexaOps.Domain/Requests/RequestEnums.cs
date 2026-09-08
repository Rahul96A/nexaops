namespace NexaOps.Domain.Requests;

/// <summary>
/// Service request lifecycle. Legal transitions live in <see cref="RequestStateMachine"/>
/// rather than being scattered through the service layer.
/// <para>
/// This is deliberately not the incident lifecycle. A request is planned work that may need
/// authorisation before anyone starts it, so it has an approval stage an incident does not, and
/// no concept of a "major" event or a resolution code.
/// </para>
/// </summary>
public enum RequestStatus
{
    Draft = 1,

    /// <summary>Submitted and waiting for one or more approvers. Fulfilment SLA does not run.</summary>
    AwaitingApproval = 2,

    /// <summary>Approved, or approval was not required. Waiting to be picked up.</summary>
    Approved = 3,

    InProgress = 4,

    /// <summary>Blocked on the requester, a vendor or a dependency. SLA clocks pause here.</summary>
    Pending = 5,

    /// <summary>Every item has been delivered.</summary>
    Fulfilled = 6,

    Closed = 7,

    /// <summary>An approver refused. Terminal - a new request is raised rather than reopening.</summary>
    Rejected = 8,

    Cancelled = 9
}

/// <summary>
/// Per-item fulfilment state. One request can contain several items that progress independently -
/// a laptop may ship while a software licence is still being procured.
/// </summary>
public enum RequestItemStatus
{
    Pending = 1,
    InProgress = 2,
    Fulfilled = 3,
    Cancelled = 4
}

/// <summary>Why a request is in <see cref="RequestStatus.Pending"/>. Drives SLA pause behaviour.</summary>
public enum RequestPendingReason
{
    AwaitingRequester = 1,
    AwaitingVendor = 2,
    AwaitingStock = 3,
    AwaitingChange = 4,
    AwaitingBudget = 5
}

/// <summary>How the request reached the service desk. Used for deflection reporting.</summary>
public enum RequestChannel
{
    Portal = 1,
    Email = 2,
    Phone = 3,
    Chat = 4,
    WalkIn = 5,
    Api = 6,
    AiAssistant = 7
}
