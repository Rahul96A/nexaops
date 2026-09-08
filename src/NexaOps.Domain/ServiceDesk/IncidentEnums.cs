namespace NexaOps.Domain.ServiceDesk;

/// <summary>
/// How badly the disruption affects the business. Impact answers "how many, how much".
/// </summary>
public enum Impact
{
    Extensive = 1,
    Significant = 2,
    Moderate = 3,
    Minor = 4
}

/// <summary>
/// How quickly the business needs it fixed. Urgency answers "how soon".
/// </summary>
public enum Urgency
{
    Critical = 1,
    High = 2,
    Medium = 3,
    Low = 4
}

/// <summary>
/// The derived work ordering. Priority is a function of impact and urgency; it is not entered
/// directly except by an explicitly recorded override.
/// </summary>
public enum Priority
{
    P1Critical = 1,
    P2High = 2,
    P3Moderate = 3,
    P4Low = 4,
    P5Planning = 5
}

/// <summary>
/// Incident lifecycle. Legal transitions are declared in <see cref="IncidentStateMachine"/>
/// rather than being scattered through the service layer.
/// </summary>
public enum IncidentStatus
{
    New = 1,
    Assigned = 2,
    InProgress = 3,

    /// <summary>Waiting on the requester, a vendor, or a change. SLA clocks pause here.</summary>
    Pending = 4,

    Resolved = 5,
    Closed = 6,
    Cancelled = 7
}

/// <summary>Why an incident is in <see cref="IncidentStatus.Pending"/>. Drives SLA pause behaviour.</summary>
public enum PendingReason
{
    AwaitingRequester = 1,
    AwaitingVendor = 2,
    AwaitingChange = 3,
    AwaitingParts = 4,
    AwaitingProblem = 5
}

/// <summary>How the incident reached the service desk. Useful for deflection reporting.</summary>
public enum IncidentChannel
{
    Portal = 1,
    Email = 2,
    Phone = 3,
    Chat = 4,
    WalkIn = 5,
    Monitoring = 6,
    Api = 7,
    AiAssistant = 8
}

/// <summary>Closure classification, required when moving to Resolved.</summary>
public enum ResolutionCode
{
    Resolved = 1,
    ResolvedByWorkaround = 2,
    ResolvedByKnownError = 3,
    ResolvedByChange = 4,
    Duplicate = 5,
    NoFaultFound = 6,
    UserEducated = 7,
    WithdrawnByRequester = 8
}

/// <summary>Distinguishes customer-visible correspondence from internal engineering notes.</summary>
public enum IncidentCommentKind
{
    /// <summary>Visible to the requester in the self-service portal.</summary>
    PublicComment = 1,

    /// <summary>Internal only. Never returned to a caller lacking incident.worknote.read.</summary>
    WorkNote = 2
}

/// <summary>How one record relates to another.</summary>
public enum RecordRelationType
{
    RelatedTo = 1,
    DuplicateOf = 2,
    CausedBy = 3,
    Causes = 4,
    BlockedBy = 5,
    Blocks = 6,
    ChildOf = 7,
    ParentOf = 8
}
