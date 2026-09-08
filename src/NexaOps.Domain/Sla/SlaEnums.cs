namespace NexaOps.Domain.Sla;

/// <summary>Which commitment an SLA instance is measuring.</summary>
public enum SlaTargetType
{
    /// <summary>Time from creation to the first agent response.</summary>
    Response = 1,

    /// <summary>Time from creation to resolution.</summary>
    Resolution = 2,

    /// <summary>Time from resolution to closure. Used where a confirmation window is contractual.</summary>
    Closure = 3
}

/// <summary>The live state of one SLA clock.</summary>
public enum SlaState
{
    /// <summary>Clock is running.</summary>
    InProgress = 1,

    /// <summary>Clock is stopped because the record is waiting on someone outside the service desk.</summary>
    Paused = 2,

    /// <summary>Target was hit before the due time.</summary>
    Met = 3,

    /// <summary>Due time passed without the target being hit.</summary>
    Breached = 4,

    /// <summary>No longer applicable, e.g. the incident was cancelled.</summary>
    Cancelled = 5
}
