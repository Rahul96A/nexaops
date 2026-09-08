using NexaOps.Application.Common;
using NexaOps.Domain.Cmdb;

namespace NexaOps.Application.Cmdb;

public sealed record CiSummaryDto(
    Guid Id,
    string Number,
    string Name,
    CiType Type,
    CiStatus Status,
    CiCriticality Criticality,
    string? Environment,
    string? Location,
    Guid? OwnerUserId,
    string? OwnerName,
    Guid? SupportGroupId,
    string? SupportGroupName,
    DateOnly? SupportExpiresOn,
    bool IsOutOfSupport,
    DateTimeOffset CreatedAt);

/// <param name="Depth">Hops from the item. Depth 1 is a direct neighbour.</param>
public sealed record RelatedItemDto(
    Guid Id,
    string Number,
    string Name,
    CiType Type,
    CiStatus Status,
    CiCriticality Criticality,
    CiRelationshipType Relationship,
    int Depth);

public sealed record CiDetailDto(
    Guid Id,
    string Number,
    string Name,
    string? Description,
    CiType Type,
    CiStatus Status,
    CiCriticality Criticality,
    string? Location,
    string? SerialNumber,
    string? Manufacturer,
    string? Model,
    string? Version,
    string? Environment,
    Guid? OwnerUserId,
    string? OwnerName,
    Guid? SupportGroupId,
    string? SupportGroupName,
    DateOnly? AcquiredOn,
    DateOnly? SupportExpiresOn,
    bool IsOutOfSupport,
    string? Vendor,
    DateTimeOffset CreatedAt,

    /// <summary>What stops working if this item fails, nearest first.</summary>
    IReadOnlyList<RelatedItemDto> Impacts,

    /// <summary>What this item relies on.</summary>
    IReadOnlyList<RelatedItemDto> DependsOn,

    /// <summary>Open incidents currently attributed to this item.</summary>
    int OpenIncidentCount,
    byte[]? RowVersion);

public sealed record CmdbSummaryCountsDto(
    int TotalItems,
    int Operational,
    int Impaired,
    int CriticalItems,
    int OutOfSupport,
    int Unowned,
    IReadOnlyList<CiTypeCountDto> ByType);

public sealed record CiTypeCountDto(CiType Type, int Count);

// ---------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------

public sealed class UpsertCiCommand
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public CiType Type { get; set; } = CiType.Server;
    public CiStatus Status { get; set; } = CiStatus.Operational;
    public CiCriticality Criticality { get; set; } = CiCriticality.Medium;
    public string? Location { get; set; }
    public string? SerialNumber { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Version { get; set; }
    public string? Environment { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid? SupportGroupId { get; set; }
    public DateOnly? AcquiredOn { get; set; }
    public DateOnly? SupportExpiresOn { get; set; }
    public string? Vendor { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class AddRelationshipCommand
{
    public Guid TargetId { get; set; }
    public CiRelationshipType Type { get; set; } = CiRelationshipType.DependsOn;
    public string? Description { get; set; }
}

public sealed class CiQuery
{
    public string? Search { get; set; }
    public List<CiType>? Types { get; set; }
    public List<CiStatus>? Statuses { get; set; }
    public List<CiCriticality>? Criticalities { get; set; }
    public string? Environment { get; set; }
    public Guid? OwnerUserId { get; set; }

    /// <summary>Named scope, e.g. <c>critical</c>, <c>out-of-support</c>, <c>unowned</c>.</summary>
    public string? Scope { get; set; }

    public string SortBy { get; set; } = "name";
    public bool SortDescending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

/// <summary>Write-side access to the CMDB.</summary>
public interface ICmdbRepository
{
    Task<ConfigurationItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken = default);

    void Add(ConfigurationItem item);

    void SetExpectedVersion(ConfigurationItem item, byte[] rowVersion);

    Task<CiRelationship?> GetRelationshipAsync(
        Guid sourceId,
        Guid targetId,
        CiRelationshipType type,
        CancellationToken cancellationToken = default);

    void AddRelationship(CiRelationship relationship);

    void RemoveRelationship(CiRelationship relationship);

    /// <summary>
    /// Every relationship in the tenant, for graph walking.
    /// <para>
    /// Loaded whole rather than walked hop by hop: a CMDB edge list is small - thousands of rows,
    /// not millions - and one query beats six round trips per record page.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<CiRelationship>> GetAllRelationshipsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Read-side queries for the CMDB.</summary>
public interface ICmdbQueryService
{
    Task<PagedResult<CiSummaryDto>> SearchAsync(CiQuery query, CancellationToken cancellationToken = default);

    Task<CiDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CmdbSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
