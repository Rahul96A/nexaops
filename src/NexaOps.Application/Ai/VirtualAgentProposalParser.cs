using System.Text.Json;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Ai;

/// <summary>
/// Splits a virtual agent reply into what to show the user and what, if anything, it is
/// proposing.
/// <para>
/// A marker line rather than a tool call, because a proposal is not an action: it is a
/// suggestion that has to survive being edited by a person before it means anything, and a tool
/// call would put it on the same footing as the read tools the model may invoke freely.
/// </para>
/// <para>
/// Pure, and forgiving in exactly one direction: anything malformed yields no proposal rather
/// than a half-parsed one. A wrong ticket raised on somebody's behalf is worse than no offer to
/// raise one, and the person can always ask in words.
/// </para>
/// </summary>
public static class VirtualAgentProposalParser
{
    /// <summary>The marker the model is instructed to emit.</summary>
    public const string Marker = "PROPOSE_TICKET:";

    /// <summary>The reply to display, and the proposal if there was a well-formed one.</summary>
    public static (string Reply, VirtualAgentProposalDto? Proposal) Split(string content)
    {
        var text = content ?? string.Empty;

        var index = text.IndexOf(Marker, StringComparison.Ordinal);

        if (index < 0)
        {
            return (text.Trim(), null);
        }

        // Everything before the marker is the reply. The marker and what follows it are never
        // shown: a user seeing raw JSON in a chat window would reasonably conclude the product
        // is broken.
        var reply = text[..index].Trim();
        var json = text[(index + Marker.Length)..].Trim();

        // Models occasionally wrap JSON in a fenced code block despite instructions. Unwrapping
        // it is a one-line accommodation of a well-known habit, not a parser.
        json = Unfence(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return (reply, null);
            }

            var title = Text(root, "title");

            // A proposal with no title is not a proposal. It would reach the user as an empty
            // confirmation card, and confirming it would fail validation anyway.
            if (string.IsNullOrWhiteSpace(title))
            {
                return (reply, null);
            }

            var description = Text(root, "description");

            // An unrecognised urgency reads as Medium rather than as the most severe option: a
            // model that writes "urgent" must not thereby escalate somebody's ticket above
            // everybody else's.
            //
            // IsDefined is not redundant. Enum.TryParse happily accepts a numeric string, so
            // "9" parses to an Urgency of 9 — a value no member has, which would then travel
            // into an incident and out to every list view that switches on it.
            var urgency = root.TryGetProperty("urgency", out var value)
                          && value.ValueKind == JsonValueKind.String
                          && Enum.TryParse<Urgency>(value.GetString(), ignoreCase: true, out var parsed)
                          && Enum.IsDefined(parsed)
                ? parsed
                : Urgency.Medium;

            return (
                reply,
                new VirtualAgentProposalDto(
                    "raise_incident",
                    title.Trim(),
                    string.IsNullOrWhiteSpace(description) ? title.Trim() : description.Trim(),
                    urgency));
        }
        catch (JsonException)
        {
            // Truncated output — the model ran out of tokens mid-marker — is the common case.
            return (reply, null);
        }
    }

    private static string Unfence(string json)
    {
        var text = json.Trim();

        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstBreak = text.IndexOf('\n');
        if (firstBreak < 0)
        {
            return text;
        }

        text = text[(firstBreak + 1)..];

        var fence = text.LastIndexOf("```", StringComparison.Ordinal);
        return (fence < 0 ? text : text[..fence]).Trim();
    }

    private static string? Text(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
