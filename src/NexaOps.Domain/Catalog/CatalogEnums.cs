namespace NexaOps.Domain.Catalog;

/// <summary>
/// Publication state of a catalogue item.
/// <para>
/// Draft items are invisible to requesters but fully editable. Retired items stay in the
/// database because existing requests reference them - a catalogue item is never deleted, or
/// historical requests would lose the definition of what was actually ordered.
/// </para>
/// </summary>
public enum CatalogItemStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3
}

/// <summary>
/// The input types a catalogue item can ask for.
/// <para>
/// Deliberately a closed set rather than arbitrary HTML. Every type has server-side validation
/// and a known storage shape; a free-form field definition would mean the server could not
/// validate what it stores.
/// </para>
/// </summary>
public enum VariableType
{
    Text = 1,
    TextArea = 2,
    Number = 3,
    Date = 4,
    Boolean = 5,

    /// <summary>Single selection from <see cref="CatalogItemVariable.ChoicesJson"/>.</summary>
    Choice = 6,

    /// <summary>Multiple selection. Stored as a JSON array of choice values.</summary>
    MultiChoice = 7,

    /// <summary>A user in the same tenant. Stored as a user id, validated against the directory.</summary>
    User = 8,

    /// <summary>A group in the same tenant. Stored as a group id.</summary>
    Group = 9
}
