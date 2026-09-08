using NexaOps.Domain.Common;

namespace NexaOps.Domain.Platform;

/// <summary>
/// Per-tenant counter behind human-facing record numbers such as INC0001042.
/// <para>
/// Allocation is done inside the caller's transaction with an update lock, which is what makes
/// the numbers gapless and collision-free under concurrency. Numbers are never reused, even
/// when a record is archived.
/// </para>
/// </summary>
public class NumberSequence : TenantEntity
{
    /// <summary>Sequence key, e.g. INC, REQ, CHG, PRB, KB, TASK.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Prefix rendered before the padded number.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>The value that will be handed out next.</summary>
    public long NextValue { get; set; } = 1;

    /// <summary>Zero-padding width, so INC0000001 sorts correctly as a string.</summary>
    public int PadWidth { get; set; } = 7;

    /// <summary>Formats a value using this sequence configuration.</summary>
    public string Format(long value)
        => Prefix + value.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(PadWidth, '0');
}
