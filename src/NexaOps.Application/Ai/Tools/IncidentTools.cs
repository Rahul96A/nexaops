using System.Text.Json;
using NexaOps.Application.Incidents;
using NexaOps.Application.Security;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Ai.Tools;

/// <summary>
/// Shared JSON options for tool payloads: compact, camel-cased, enums as readable names so the
/// model sees <c>P1Critical</c> rather than <c>1</c>.
/// </summary>
internal static class AiToolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Reads an optional string property, treating null and whitespace as absent.</summary>
    public static string? OptionalString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    public static int OptionalInt(JsonElement root, string name, int fallback)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : fallback;

    public static bool? OptionalBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
           && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    /// <summary>
    /// Parses an enum name case-insensitively, returning null for anything unrecognised.
    /// <para>
    /// The <c>IsDefined</c> check is load-bearing: <c>Enum.TryParse</c> accepts a numeric string,
    /// so a model passing <c>"9"</c> as a priority would otherwise produce a Priority of 9 — a
    /// value no member has, which then reaches a query and every view that switches on it.
    /// </para>
    /// </summary>
    public static TEnum? OptionalEnum<TEnum>(JsonElement root, string name) where TEnum : struct, Enum
    {
        var raw = OptionalString(root, name);

        return raw is not null
               && Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed)
               && Enum.IsDefined(parsed)
            ? parsed
            : null;
    }
}

/// <summary>
/// Searches incidents in the caller's tenant, honouring their visibility scope.
/// <para>
/// The tool calls the same <see cref="IIncidentService"/> the REST API uses, so a user who
/// cannot see an incident through the UI cannot see it through the assistant either.
/// </para>
/// </summary>
public sealed class SearchIncidentsTool : IAiTool
{
    private readonly IIncidentService _incidents;

    public SearchIncidentsTool(IIncidentService incidents) => _incidents = incidents;

    public string Name => "search_incidents";

    public string Description =>
        "Search incidents in the current organisation. Use this to answer questions about " +
        "how many incidents exist, which are critical, which have breached their SLA, what a " +
        "team or an individual is working on, or what was raised in a date range. " +
        "Returns a page of matching incidents with their number, title, priority and status.";

    public string RequiredPermission => Permissions.IncidentRead;

    public bool IsMutating => false;

    public JsonElement InputSchema { get; } = AiToolJson.Schema(
        """
        {
          "type": "object",
          "properties": {
            "search":       { "type": "string", "description": "Free text matched against number, title and description." },
            "status":       { "type": "string", "enum": ["New","Assigned","InProgress","Pending","Resolved","Closed","Cancelled"] },
            "priority":     { "type": "string", "enum": ["P1Critical","P2High","P3Moderate","P4Low","P5Planning"] },
            "openOnly":     { "type": "boolean", "description": "Restrict to incidents that are still being worked." },
            "breachedOnly": { "type": "boolean", "description": "Restrict to incidents that have breached an SLA." },
            "majorOnly":    { "type": "boolean", "description": "Restrict to declared major incidents." },
            "scope":        { "type": "string", "enum": ["All","AssignedToMe","MyTeam","RaisedByMe","Unassigned","Breached","DueSoon"] },
            "createdAfter":  { "type": "string", "format": "date-time" },
            "createdBefore": { "type": "string", "format": "date-time" },
            "limit":        { "type": "integer", "minimum": 1, "maximum": 50, "default": 20 }
          },
          "additionalProperties": false
        }
        """);

    public async Task<AiToolResult> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var query = new IncidentQuery
        {
            Search = AiToolJson.OptionalString(arguments, "search"),
            OpenOnly = AiToolJson.OptionalBool(arguments, "openOnly"),
            HasBreachedSla = AiToolJson.OptionalBool(arguments, "breachedOnly"),
            IsMajorIncident = AiToolJson.OptionalBool(arguments, "majorOnly"),
            Scope = AiToolJson.OptionalEnum<IncidentViewScope>(arguments, "scope") ?? IncidentViewScope.All,
            PageSize = Math.Clamp(AiToolJson.OptionalInt(arguments, "limit", 20), 1, 50),
            SortBy = "createdAt"
        };

        if (AiToolJson.OptionalEnum<IncidentStatus>(arguments, "status") is { } status)
        {
            query.Statuses = [status];
        }

        if (AiToolJson.OptionalEnum<Priority>(arguments, "priority") is { } priority)
        {
            query.Priorities = [priority];
        }

        if (AiToolJson.OptionalString(arguments, "createdAfter") is { } after
            && DateTimeOffset.TryParse(after, out var from))
        {
            query.CreatedFrom = from;
        }

        if (AiToolJson.OptionalString(arguments, "createdBefore") is { } before
            && DateTimeOffset.TryParse(before, out var to))
        {
            query.CreatedTo = to;
        }

        var page = await _incidents.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(
            new
            {
                totalMatching = page.TotalCount,
                returned = page.Items.Count,
                incidents = page.Items.Select(i => new
                {
                    i.Number,
                    i.Title,
                    Status = i.Status.ToString(),
                    Priority = i.Priority.ToString(),
                    Category = i.CategoryName,
                    Requester = i.RequesterName,
                    AssignedTo = i.AssignedToName,
                    Group = i.AssignmentGroupName,
                    i.HasBreachedSla,
                    i.IsMajorIncident,
                    CreatedAt = i.CreatedAt.ToString("u"),
                    ResolvedAt = i.ResolvedAt?.ToString("u")
                })
            },
            AiToolJson.Options);

        return AiToolResult.Ok(
            payload,
            $"search_incidents returned {page.Items.Count} of {page.TotalCount} matching incidents.");
    }
}

/// <summary>Fetches one incident in full, so the assistant can summarise or explain it.</summary>
public sealed class GetIncidentTool : IAiTool
{
    private readonly IIncidentService _incidents;

    public GetIncidentTool(IIncidentService incidents) => _incidents = incidents;

    public string Name => "get_incident";

    public string Description =>
        "Retrieve the full detail of one incident by its number, for example INC0001042. " +
        "Use this to summarise an incident, explain its priority, or report its SLA position. " +
        "Returns classification, ownership, status, resolution and SLA clocks.";

    public string RequiredPermission => Permissions.IncidentRead;

    public bool IsMutating => false;

    public JsonElement InputSchema { get; } = AiToolJson.Schema(
        """
        {
          "type": "object",
          "properties": {
            "number": { "type": "string", "description": "The incident number, e.g. INC0001042." }
          },
          "required": ["number"],
          "additionalProperties": false
        }
        """);

    public async Task<AiToolResult> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var number = AiToolJson.OptionalString(arguments, "number");
        if (number is null)
        {
            return AiToolResult.Failed("An incident number is required.");
        }

        var incident = await _incidents.GetByNumberAsync(number.Trim(), cancellationToken)
            .ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(
            new
            {
                incident.Number,
                incident.Title,
                incident.Description,
                Status = incident.Status.ToString(),
                Priority = incident.Priority.ToString(),
                Impact = incident.Impact.ToString(),
                Urgency = incident.Urgency.ToString(),
                incident.IsPriorityOverridden,
                incident.PriorityOverrideReason,
                Category = incident.CategoryName,
                Subcategory = incident.SubcategoryName,
                Requester = incident.RequesterName,
                AffectedUser = incident.AffectedUserName,
                Organization = incident.OrganizationName,
                Department = incident.DepartmentName,
                AssignmentGroup = incident.AssignmentGroupName,
                AssignedTo = incident.AssignedToName,
                incident.IsMajorIncident,
                incident.ReopenCount,
                Resolution = incident.ResolutionCode?.ToString(),
                incident.ResolutionNotes,
                CreatedAt = incident.CreatedAt.ToString("u"),
                FirstRespondedAt = incident.FirstRespondedAt?.ToString("u"),
                ResolvedAt = incident.ResolvedAt?.ToString("u"),
                Tags = incident.Tags,
                SlaClocks = incident.SlaInstances.Select(s => new
                {
                    s.Name,
                    Target = s.TargetType.ToString(),
                    State = s.State.ToString(),
                    DueAt = s.DueAt.ToString("u"),
                    s.ElapsedMinutes,
                    s.RemainingMinutes,
                    s.ConsumedPercent
                })
            },
            AiToolJson.Options);

        return AiToolResult.Ok(payload, $"get_incident retrieved {incident.Number}.");
    }
}

/// <summary>Returns live service desk counters so the assistant can answer "how are we doing".</summary>
public sealed class GetServiceDeskSummaryTool : IAiTool
{
    private readonly IIncidentService _incidents;

    public GetServiceDeskSummaryTool(IIncidentService incidents) => _incidents = incidents;

    public string Name => "get_service_desk_summary";

    public string Description =>
        "Get live service desk counters: open incidents, critical and high counts, SLA breaches, " +
        "unassigned work, incidents created and resolved today, and per-agent workload. " +
        "Use this for questions about overall queue health rather than about a specific incident.";

    public string RequiredPermission => Permissions.IncidentRead;

    public bool IsMutating => false;

    public JsonElement InputSchema { get; } = AiToolJson.Schema(
        """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """);

    public async Task<AiToolResult> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var summary = await _incidents.GetServiceDeskSummaryAsync(cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(
            new
            {
                summary.OpenIncidents,
                summary.CriticalOpen,
                summary.HighOpen,
                summary.UnassignedInMyGroups,
                summary.AssignedToMe,
                summary.BreachedOpen,
                summary.DueWithinTwoHours,
                summary.CreatedToday,
                summary.ResolvedToday,
                OpenByPriority = summary.OpenByPriority.Select(p => new
                {
                    Priority = p.Priority.ToString(),
                    p.Count
                }),
                OpenByStatus = summary.OpenByStatus.Select(s => new
                {
                    Status = s.Status.ToString(),
                    s.Count
                }),
                TeamWorkload = summary.TeamWorkload.Select(w => new
                {
                    Agent = w.DisplayName,
                    w.OpenCount,
                    w.BreachedCount
                })
            },
            AiToolJson.Options);

        return AiToolResult.Ok(payload, "get_service_desk_summary returned live queue counters.");
    }
}
