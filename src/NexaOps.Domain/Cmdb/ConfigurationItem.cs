using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;

namespace NexaOps.Domain.Cmdb;

/// <summary>
/// What kind of thing a configuration item is.
/// <para>
/// A closed set rather than free text. The value of a CMDB is that you can ask "what runs on
/// this server" and get an answer; a taxonomy anyone can extend by typing produces three
/// spellings of "database" and no answers.
/// </para>
/// </summary>
public enum CiType
{
    // Infrastructure
    Server = 1,
    VirtualMachine = 2,
    NetworkDevice = 3,
    StorageDevice = 4,

    // Platform
    Database = 10,
    Application = 11,
    Middleware = 12,

    // Service-facing
    BusinessService = 20,
    TechnicalService = 21,

    // Endpoints
    Workstation = 30,
    MobileDevice = 31,
    Printer = 32,

    // Contracts and licences, which are configuration items in the sense that services depend
    // on them and they expire.
    SoftwareLicence = 40,
    CloudResource = 41
}

/// <summary>Operational state of a configuration item.</summary>
public enum CiStatus
{
    /// <summary>Being built or procured. Not yet carrying anything.</summary>
    Planned = 1,

    /// <summary>Live and in service.</summary>
    Operational = 2,

    /// <summary>Live but degraded or under maintenance.</summary>
    Impaired = 3,

    /// <summary>Off, but retained and re-usable.</summary>
    Retired = 4,

    /// <summary>Physically gone or contract ended.</summary>
    Disposed = 5
}

/// <summary>
/// How much of the business stops if this item does. Drives impact analysis.
/// </summary>
public enum CiCriticality
{
    Low = 1,
    Medium = 2,
    High = 3,

    /// <summary>The business stops. A short list, or the classification means nothing.</summary>
    Critical = 4
}

/// <summary>
/// An item in the configuration management database: a server, a service, a database, a licence.
/// <para>
/// The module exists to answer two questions an outage forces: what does this thing support, and
/// what does it depend on. Everything else here serves those.
/// </para>
/// </summary>
public class ConfigurationItem : TenantEntity
{
    /// <summary>Human-facing identifier, e.g. CI0000042. Unique per tenant, never reused.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>The name people actually use for it, e.g. <c>sql-prod-01</c>.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public CiType Type { get; set; } = CiType.Server;
    public CiStatus Status { get; set; } = CiStatus.Operational;
    public CiCriticality Criticality { get; set; } = CiCriticality.Medium;

    /// <summary>Where it physically or logically lives, e.g. <c>Bengaluru DC</c>, <c>Azure Central India</c>.</summary>
    public string? Location { get; set; }

    /// <summary>Serial number, asset tag, cloud resource id — whatever identifies the instance.</summary>
    public string? SerialNumber { get; set; }

    public string? Manufacturer { get; set; }
    public string? Model { get; set; }

    /// <summary>Version or build, where the item has one.</summary>
    public string? Version { get; set; }

    /// <summary>
    /// The environment it belongs to. Free text rather than an enum: customers genuinely differ,
    /// and "UAT2" is a real answer this product should not argue with.
    /// </summary>
    public string? Environment { get; set; }

    // --- Ownership ---

    /// <summary>The person accountable for it. An unowned CI is one nobody maintains.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>The group that supports it, and who an incident against it routes to.</summary>
    public Guid? SupportGroupId { get; set; }

    // --- Commercial ---
    public DateOnly? AcquiredOn { get; set; }

    /// <summary>
    /// When support or licence cover ends. Surfaced because an expired warranty discovered
    /// during an outage is the most expensive way to learn about it.
    /// </summary>
    public DateOnly? SupportExpiresOn { get; set; }

    public string? Vendor { get; set; }

    // --- Navigation ---
    public User? Owner { get; set; }
    public Group? SupportGroup { get; set; }

    public ICollection<CiRelationship> OutgoingRelationships { get; set; } = new List<CiRelationship>();

    /// <summary>True when the item is live and expected to work.</summary>
    public bool IsLive => Status is CiStatus.Operational or CiStatus.Impaired;

    /// <summary>True when support cover has lapsed as at the given date.</summary>
    public bool IsOutOfSupport(DateOnly asOf)
        => SupportExpiresOn is not null && SupportExpiresOn < asOf;

    /// <summary>
    /// Moves the item to a new state.
    /// <para>
    /// Deliberately not a state machine. A CMDB reflects reality rather than governing it: a
    /// server that was disposed of and then found in a cupboard really can go back to
    /// operational, and refusing that would only teach people to keep a spreadsheet instead.
    /// </para>
    /// </summary>
    public void ChangeStatus(CiStatus next) => Status = next;
}

/// <summary>How one configuration item relates to another.</summary>
public enum CiRelationshipType
{
    /// <summary>The source cannot work without the target. The relationship impact analysis walks.</summary>
    DependsOn = 1,

    /// <summary>The source physically or logically contains the target.</summary>
    Contains = 2,

    /// <summary>The source runs on the target.</summary>
    RunsOn = 3,

    /// <summary>The source connects to the target over a network.</summary>
    ConnectsTo = 4,

    /// <summary>The source is a redundant partner of the target.</summary>
    FailsOverTo = 5
}

/// <summary>
/// A directed edge between two configuration items.
/// <para>
/// Direction matters and is not symmetric: a database depending on a server is a very different
/// statement from a server depending on a database, and impact analysis walks the edge one way
/// only. Storing it once with a direction, rather than twice, keeps the two from disagreeing.
/// </para>
/// </summary>
public class CiRelationship : TenantEntity
{
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }

    public CiRelationshipType Type { get; set; } = CiRelationshipType.DependsOn;

    /// <summary>Optional note, e.g. which port or which mount.</summary>
    public string? Description { get; set; }

    public ConfigurationItem? Source { get; set; }
    public ConfigurationItem? Target { get; set; }

    /// <summary>
    /// Guards the one invariant a graph edge has: an item cannot relate to itself.
    /// </summary>
    public void EnsureValid()
    {
        if (SourceId == TargetId)
        {
            throw new DomainException(
                "cmdb.self_relationship",
                "A configuration item cannot be related to itself.");
        }
    }
}
