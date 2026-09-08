using NexaOps.Domain.Common;

namespace NexaOps.Domain.Platform;

/// <summary>
/// A tenant-scoped configuration value edited from the administration UI.
/// <para>
/// This store is for behaviour a customer tunes (business rules, thresholds, feature toggles),
/// never for secrets. Credentials live in Azure Key Vault; a value written here is readable by
/// any tenant administrator and must be treated as such.
/// </para>
/// </summary>
public class SystemSetting : TenantEntity
{
    /// <summary>Dotted key, e.g. incident.auto_close_resolved_after_days.</summary>
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public SettingValueType ValueType { get; set; } = SettingValueType.String;

    /// <summary>Grouping for the administration UI, e.g. Incident, Sla, Notifications, Ai.</summary>
    public string Category { get; set; } = "General";

    public string? DisplayName { get; set; }
    public string? Description { get; set; }

    /// <summary>Platform-managed settings are visible to tenant admins but not editable by them.</summary>
    public bool IsReadOnly { get; set; }
}

public enum SettingValueType
{
    String = 1,
    Integer = 2,
    Boolean = 3,
    Decimal = 4,
    Json = 5
}
