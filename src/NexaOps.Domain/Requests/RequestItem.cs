using System.Text.Json;
using NexaOps.Domain.Catalog;
using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;

namespace NexaOps.Domain.Requests;

/// <summary>
/// One line on a service request: what was ordered, how many, and the answers given.
/// <para>
/// The line <em>snapshots</em> the catalogue item's name and cost at the moment of ordering.
/// That is deliberate. A catalogue price or label edited six months later must not silently
/// rewrite what somebody ordered and what was approved on that basis.
/// </para>
/// </summary>
public class RequestItem : TenantEntity
{
    public Guid ServiceRequestId { get; set; }

    /// <summary>The template this was ordered from. Retained for reporting; never re-read for values.</summary>
    public Guid CatalogItemId { get; set; }

    /// <summary>Item name as it was at the time of ordering.</summary>
    public string CatalogItemName { get; set; } = string.Empty;

    /// <summary>Unit cost as it was at the time of ordering.</summary>
    public decimal? UnitCost { get; set; }

    public int Quantity { get; set; } = 1;

    public RequestItemStatus Status { get; set; } = RequestItemStatus.Pending;

    /// <summary>
    /// The submitted answers, as a JSON object keyed by
    /// <see cref="CatalogItemVariable.Key"/>. Stored as JSON rather than as rows because the
    /// shape is defined by the catalogue item, not by the schema, and nothing queries across
    /// individual answers.
    /// </summary>
    public string VariableValuesJson { get; set; } = "{}";

    /// <summary>Group fulfilling this line. Copied from the catalogue item, then editable.</summary>
    public Guid? FulfilmentGroupId { get; set; }

    public Guid? AssignedToUserId { get; set; }

    public DateTimeOffset? FulfilledAt { get; set; }
    public Guid? FulfilledByUserId { get; set; }

    /// <summary>Note recorded on delivery, e.g. an asset tag or a licence key reference.</summary>
    public string? FulfilmentNotes { get; set; }

    // --- Navigation ---
    public ServiceRequest? ServiceRequest { get; set; }
    public CatalogItem? CatalogItem { get; set; }
    public Group? FulfilmentGroup { get; set; }
    public User? AssignedTo { get; set; }

    /// <summary>Line total at the price captured when it was ordered.</summary>
    public decimal? LineCost => UnitCost is null ? null : UnitCost * Quantity;

    /// <summary>The submitted answers, or an empty map when none were required.</summary>
    public IReadOnlyDictionary<string, string> VariableValues()
    {
        if (string.IsNullOrWhiteSpace(VariableValuesJson))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(VariableValuesJson)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            // Stored values are written by the application after validation, so this should not
            // happen. Returning empty keeps a record page rendering rather than throwing on a
            // detail panel.
            return new Dictionary<string, string>();
        }
    }

    /// <summary>Marks the line delivered.</summary>
    public void Fulfil(Guid actorUserId, DateTimeOffset now, string? notes)
    {
        if (Status is RequestItemStatus.Fulfilled or RequestItemStatus.Cancelled)
        {
            throw new DomainException(
                "request.item_already_settled",
                $"This item has already been {Status.ToString().ToLowerInvariant()}.");
        }

        Status = RequestItemStatus.Fulfilled;
        FulfilledAt = now;
        FulfilledByUserId = actorUserId;
        FulfilmentNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
