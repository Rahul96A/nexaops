using System.Text.Json;
using NexaOps.Application.Knowledge;
using NexaOps.Application.Requests;
using NexaOps.Application.Security;
using NexaOps.Domain.Knowledge;

namespace NexaOps.Application.Ai.Tools;

/// <summary>
/// Finds published knowledge articles.
/// <para>
/// The most valuable tool the virtual agent has. Most of what an employee contacts a service
/// desk about has already been answered in writing; the reason they contact the desk anyway is
/// that finding the answer is harder than asking a person. Deflection here is not a cost-saving
/// trick — it is a faster answer for them and a shorter queue for everybody else.
/// </para>
/// </summary>
public sealed class SearchKnowledgeTool : IAiTool
{
    private readonly IKnowledgeService _knowledge;

    public SearchKnowledgeTool(IKnowledgeService knowledge) => _knowledge = knowledge;

    public string Name => "search_knowledge";

    public string Description =>
        "Search published knowledge base articles for guidance on how to do something or fix "
        + "something. Use this FIRST whenever the user describes a problem or asks how to do "
        + "something, before offering to raise a ticket. Returns article numbers, titles and "
        + "summaries.";

    public string RequiredPermission => Permissions.KnowledgeRead;

    public bool IsMutating => false;

    public JsonElement InputSchema => AiToolJson.Schema(
        """
        {
          "type": "object",
          "properties": {
            "search": {
              "type": "string",
              "description": "Words from the user's question, e.g. 'vpn not connecting'."
            },
            "limit": { "type": "integer", "description": "Maximum articles to return. Default 5." }
          },
          "required": ["search"]
        }
        """);

    public async Task<AiToolResult> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var query = new ArticleQuery
        {
            Search = AiToolJson.OptionalString(arguments, "search"),

            // Published only. A draft is somebody's unfinished thinking and a retired article is
            // guidance that was withdrawn on purpose; either would be worse than no answer.
            Statuses = [ArticleStatus.Published],
            PageSize = Math.Clamp(AiToolJson.OptionalInt(arguments, "limit", 5), 1, 10)
        };

        var results = await _knowledge.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(
            new
            {
                total = results.TotalCount,
                articles = results.Items.Select(a => new
                {
                    a.Number,
                    a.Title,
                    a.Summary,
                    category = a.CategoryName,

                    // Surfaced so the assistant can flag guidance that is overdue for review
                    // rather than presenting old advice as current.
                    reviewDueAt = a.ReviewDueAt,
                    helpfulRatio = a.HelpfulRatio
                })
            },
            AiToolJson.Options);

        return AiToolResult.Ok(payload);
    }
}

/// <summary>
/// Lists the caller's own service requests.
/// <para>
/// Answers "where has my laptop request got to", which is the single most common reason an
/// employee chases the service desk. The underlying service scopes to what the caller may see,
/// so this cannot be turned into a way to read somebody else's requests.
/// </para>
/// </summary>
public sealed class GetMyRequestsTool : IAiTool
{
    private readonly IRequestService _requests;

    public GetMyRequestsTool(IRequestService requests) => _requests = requests;

    public string Name => "get_my_requests";

    public string Description =>
        "List the service requests raised by the person you are talking to, newest first. Use "
        + "this when they ask about the status or progress of something they have ordered or "
        + "asked for.";

    public string RequiredPermission => Permissions.RequestRead;

    public bool IsMutating => false;

    public JsonElement InputSchema => AiToolJson.Schema(
        """
        {
          "type": "object",
          "properties": {
            "openOnly": {
              "type": "boolean",
              "description": "Exclude closed and cancelled requests. Default true."
            },
            "limit": { "type": "integer", "description": "Maximum requests to return. Default 10." }
          }
        }
        """);

    public async Task<AiToolResult> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var query = new RequestQuery
        {
            // "mine" is resolved server-side from the authenticated identity, never from an
            // argument the model supplies. A model that hallucinated a user id would get
            // nowhere: there is no parameter for one.
            Scope = "mine",
            OpenOnly = AiToolJson.OptionalBool(arguments, "openOnly") ?? true,
            PageSize = Math.Clamp(AiToolJson.OptionalInt(arguments, "limit", 10), 1, 25)
        };

        var results = await _requests.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(
            new
            {
                total = results.TotalCount,
                requests = results.Items.Select(r => new
                {
                    r.Number,
                    r.Title,
                    status = r.Status.ToString(),
                    priority = r.Priority.ToString(),
                    raisedOn = r.CreatedAt,
                    dueBy = r.NextSlaDueAt,
                    breached = r.HasBreachedSla,
                    assignedTo = r.AssignedToName
                })
            },
            AiToolJson.Options);

        return AiToolResult.Ok(payload);
    }
}

/// <summary>
/// Lists what the caller can order from the service catalogue.
/// <para>
/// Paired with the knowledge search: when somebody says "I need a new laptop", the honest answer
/// is not to raise an incident but to point at the catalogue item that exists for exactly that,
/// with its approval and delivery expectations attached.
/// </para>
/// </summary>
public sealed class SearchCatalogTool : IAiTool
{
    private readonly ICatalogService _catalog;

    public SearchCatalogTool(ICatalogService catalog) => _catalog = catalog;

    public string Name => "search_catalog";

    public string Description =>
        "Search the service catalogue for things the user can request — hardware, software "
        + "access, onboarding. Use this when they want something provided rather than something "
        + "fixed. Returns item names, what they cost and whether approval is needed.";

    public string RequiredPermission => Permissions.CatalogRead;

    public bool IsMutating => false;

    public JsonElement InputSchema => AiToolJson.Schema(
        """
        {
          "type": "object",
          "properties": {
            "search": { "type": "string", "description": "What the user is asking for." },
            "limit": { "type": "integer", "description": "Maximum items to return. Default 5." }
          },
          "required": ["search"]
        }
        """);

    public async Task<AiToolResult> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(AiToolJson.OptionalInt(arguments, "limit", 5), 1, 10);

        var results = await _catalog
            .BrowseAsync(AiToolJson.OptionalString(arguments, "search"), null, cancellationToken)
            .ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(
            new
            {
                total = results.Count,
                items = results.Take(limit).Select(i => new
                {
                    i.Id,
                    i.Name,
                    i.ShortDescription,
                    i.Cost,
                    requiresApproval = i.RequiresApproval,
                    deliveryDays = i.EstimatedDeliveryDays
                })
            },
            AiToolJson.Options);

        return AiToolResult.Ok(payload);
    }
}
