using NexaOps.Domain.Common;

namespace NexaOps.Domain.ServiceDesk;

/// <summary>
/// A typed link between two records, possibly of different modules - an incident caused by a
/// change, a problem that spawned several incidents.
/// <para>
/// Modelled generically rather than as a dedicated table per pair so that later modules
/// (problem, change, CMDB) reuse the same relationship store and the same UI component.
/// </para>
/// </summary>
public class RecordRelation : TenantEntity
{
    public ServiceModule SourceModule { get; set; }
    public Guid SourceId { get; set; }

    public ServiceModule TargetModule { get; set; }
    public Guid TargetId { get; set; }

    public RecordRelationType RelationType { get; set; } = RecordRelationType.RelatedTo;

    /// <summary>Optional human explanation of why the two records are linked.</summary>
    public string? Note { get; set; }

    /// <summary>The mirror of a relation type, used to present the link from the other side.</summary>
    public static RecordRelationType Invert(RecordRelationType type) => type switch
    {
        RecordRelationType.CausedBy => RecordRelationType.Causes,
        RecordRelationType.Causes => RecordRelationType.CausedBy,
        RecordRelationType.BlockedBy => RecordRelationType.Blocks,
        RecordRelationType.Blocks => RecordRelationType.BlockedBy,
        RecordRelationType.ChildOf => RecordRelationType.ParentOf,
        RecordRelationType.ParentOf => RecordRelationType.ChildOf,
        _ => type
    };
}
