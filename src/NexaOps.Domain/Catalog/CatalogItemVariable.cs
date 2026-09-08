using System.Globalization;
using System.Text.Json;
using NexaOps.Domain.Common;

namespace NexaOps.Domain.Catalog;

/// <summary>
/// One question a catalogue item asks before it can be ordered - "which model?", "which cost
/// centre?", "start date".
/// <para>
/// The validation lives here, in the domain, rather than only in the React form. A form is a
/// convenience for the person filling it in; it is not a control. Anything that reaches the API
/// is validated against this definition regardless of what the browser did.
/// </para>
/// </summary>
public class CatalogItemVariable : TenantEntity
{
    public Guid CatalogItemId { get; set; }

    /// <summary>Stable key the answer is stored under, e.g. <c>cost_centre</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Label shown to the requester.</summary>
    public string Label { get; set; } = string.Empty;

    public string? HelpText { get; set; }

    public VariableType Type { get; set; } = VariableType.Text;

    public bool IsRequired { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Prefilled value. Stored as a string in the same shape the answer takes.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// JSON array of allowed values for <see cref="VariableType.Choice"/> and
    /// <see cref="VariableType.MultiChoice"/>, e.g. <c>["Windows","macOS"]</c>.
    /// </summary>
    public string? ChoicesJson { get; set; }

    /// <summary>Upper bound on text length. Applies to Text and TextArea.</summary>
    public int? MaxLength { get; set; }

    /// <summary>Inclusive numeric bounds. Apply to <see cref="VariableType.Number"/>.</summary>
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }

    public CatalogItem? CatalogItem { get; set; }

    /// <summary>Parsed choices, or empty when none are configured.</summary>
    public IReadOnlyList<string> Choices()
    {
        if (string.IsNullOrWhiteSpace(ChoicesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(ChoicesJson) ?? [];
        }
        catch (JsonException)
        {
            // A malformed choice list is a configuration error, not a request error. Returning
            // empty makes Validate reject any answer with a clear message, rather than throwing
            // out of a getter halfway through rendering a catalogue page.
            return [];
        }
    }

    /// <summary>
    /// Validates one submitted answer against this definition.
    /// </summary>
    /// <param name="value">The raw submitted value, or null when the field was left blank.</param>
    /// <returns>An error message, or null when the answer is acceptable.</returns>
    public string? Validate(string? value)
    {
        var isBlank = string.IsNullOrWhiteSpace(value);

        if (isBlank)
        {
            return IsRequired ? $"{Label} is required." : null;
        }

        var trimmed = value!.Trim();

        switch (Type)
        {
            case VariableType.Text:
            case VariableType.TextArea:
                if (MaxLength is > 0 && trimmed.Length > MaxLength)
                {
                    return $"{Label} must be {MaxLength} characters or fewer.";
                }

                break;

            case VariableType.Number:
                if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    return $"{Label} must be a number.";
                }

                if (MinValue is not null && number < MinValue)
                {
                    return $"{Label} must be at least {MinValue}.";
                }

                if (MaxValue is not null && number > MaxValue)
                {
                    return $"{Label} must be at most {MaxValue}.";
                }

                break;

            case VariableType.Date:
                if (!DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, out _))
                {
                    return $"{Label} must be a date in yyyy-MM-dd format.";
                }

                break;

            case VariableType.Boolean:
                if (!bool.TryParse(trimmed, out _))
                {
                    return $"{Label} must be true or false.";
                }

                break;

            case VariableType.Choice:
            {
                var choices = Choices();
                if (choices.Count == 0)
                {
                    return $"{Label} has no configured options, so it cannot be answered.";
                }

                if (!choices.Contains(trimmed, StringComparer.Ordinal))
                {
                    return $"{Label} must be one of the offered options.";
                }

                break;
            }

            case VariableType.MultiChoice:
            {
                var choices = Choices();
                if (choices.Count == 0)
                {
                    return $"{Label} has no configured options, so it cannot be answered.";
                }

                List<string>? selected;
                try
                {
                    selected = JsonSerializer.Deserialize<List<string>>(trimmed);
                }
                catch (JsonException)
                {
                    return $"{Label} must be a list of the offered options.";
                }

                if (selected is null || selected.Count == 0)
                {
                    return IsRequired ? $"{Label} is required." : null;
                }

                if (selected.Any(s => !choices.Contains(s, StringComparer.Ordinal)))
                {
                    return $"{Label} contains an option that is not offered.";
                }

                break;
            }

            case VariableType.User:
            case VariableType.Group:
                // The reference must exist in this tenant, which the domain cannot check without
                // a repository. The application service verifies it through a tenant-filtered
                // query, exactly as incident assignment does.
                if (!Guid.TryParse(trimmed, out _))
                {
                    return $"{Label} must be a valid selection.";
                }

                break;

            default:
                return $"{Label} has an unrecognised field type.";
        }

        return null;
    }
}
