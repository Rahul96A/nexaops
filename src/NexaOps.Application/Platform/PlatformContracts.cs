using FluentValidation;
using NexaOps.Domain.Identity;

namespace NexaOps.Application.Platform;

/// <summary>One customer as the platform operator sees them in a list.</summary>
public sealed record TenantSummaryDto(
    Guid Id,
    string Code,
    string Name,
    string? LegalName,
    string Status,
    string? PrimaryDomain,
    string DataRegion,
    DateTimeOffset CreatedAt,
    int UserCount,
    int ActiveUserCount);

/// <summary>One customer in full, including the configuration a support conversation needs.</summary>
public sealed record TenantDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? LegalName,
    string Status,
    string? PrimaryDomain,
    string? EntraTenantId,
    string TimeZoneId,
    string Locale,
    string CurrencyCode,
    string DateFormat,
    string DataRegion,
    int RecordRetentionDays,
    int AuditRetentionDays,
    DateTimeOffset CreatedAt,
    int UserCount,
    int ActiveUserCount,
    DateTimeOffset? LastSignInAt);

/// <summary>
/// Onboards a customer: the tenant itself plus the one administrator who can then do everything
/// else from inside the product.
/// </summary>
public sealed class CreateTenantCommand
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? PrimaryDomain { get; set; }

    /// <summary>
    /// Trial or Active. Suspended and Closed are refused: a tenant nobody can sign in to is not
    /// a state worth creating, and it would look like a successful onboarding that silently is
    /// not one.
    /// </summary>
    public TenantStatus Status { get; set; } = TenantStatus.Trial;

    // --- The first administrator ---
    public string AdministratorEmail { get; set; } = string.Empty;
    public string AdministratorFirstName { get; set; } = string.Empty;
    public string AdministratorLastName { get; set; } = string.Empty;
    public string? AdministratorJobTitle { get; set; }
}

/// <summary>Editable tenant settings. The code is deliberately absent — see the service.</summary>
public sealed class UpdateTenantCommand
{
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? PrimaryDomain { get; set; }
    public string? EntraTenantId { get; set; }
    public int RecordRetentionDays { get; set; } = 2555;
    public int AuditRetentionDays { get; set; } = 2555;
}

/// <summary>Moves a tenant between lifecycle states.</summary>
public sealed class SetTenantStatusCommand
{
    public TenantStatus Status { get; set; }

    /// <summary>Why. Recorded in the audit trail, because suspension is a commercial act.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// The result of onboarding, including the administrator's single-use password.
/// <para>
/// The password is returned exactly once, in this response, and is never retrievable again. It
/// is not written to the audit trail or the log.
/// </para>
/// </summary>
public sealed record TenantOnboardingResult(
    TenantDetailDto Tenant,
    Guid AdministratorUserId,
    string AdministratorEmail,
    string? TemporaryPassword);

/// <summary>Filter for the tenant list.</summary>
public sealed class TenantQuery : Common.PagedQuery
{
    public TenantStatus? Status { get; set; }

    /// <summary>Free-text match over code, name and legal name.</summary>
    public string? Search { get; set; }
}

// ---------------------------------------------------------------------------
// Validation
// ---------------------------------------------------------------------------

/// <summary>
/// The tenant code is the strictest field in the product. It appears in URLs and in support
/// conversations, it is matched case-insensitively at sign-in, and it can never be changed, so
/// the rules are tighter than they would need to be for storage alone.
/// </summary>
public sealed class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("A tenant code is required.")
            .Length(3, 32).WithMessage("A tenant code is between 3 and 32 characters.")
            .Matches("^[a-z0-9][a-z0-9-]*[a-z0-9]$")
            .WithMessage("A tenant code uses lower-case letters, digits and hyphens, and starts and ends with a letter or digit.")
            .Must(code => !code.Contains("--", StringComparison.Ordinal))
            .WithMessage("A tenant code cannot contain two consecutive hyphens.")
            .Must(code => !ReservedCodes.Contains(code))
            .WithMessage("That tenant code is reserved.");

        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.LegalName).MaximumLength(300);
        RuleFor(x => x.PrimaryDomain).MaximumLength(255);

        RuleFor(x => x.Status)
            .Must(status => status is TenantStatus.Active or TenantStatus.Trial)
            .WithMessage("A new tenant starts as Trial or Active.");

        RuleFor(x => x.AdministratorEmail)
            .NotEmpty().WithMessage("The first administrator's email address is required.")
            .EmailAddress()
            .MaximumLength(320);

        RuleFor(x => x.AdministratorFirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AdministratorLastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AdministratorJobTitle).MaximumLength(150);
    }

    /// <summary>
    /// Codes that would collide with a route segment or read as the platform itself. Refused up
    /// front rather than discovered when a URL stops resolving.
    /// </summary>
    private static readonly HashSet<string> ReservedCodes = new(StringComparer.Ordinal)
    {
        "api", "app", "admin", "platform", "system", "nexaops", "www", "health", "static", "assets"
    };
}

public sealed class UpdateTenantCommandValidator : AbstractValidator<UpdateTenantCommand>
{
    public UpdateTenantCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.LegalName).MaximumLength(300);
        RuleFor(x => x.PrimaryDomain).MaximumLength(255);
        RuleFor(x => x.EntraTenantId).MaximumLength(64);

        // One day to twenty years. The upper bound is not arbitrary: a retention window longer
        // than the company is likely to exist reads as "never delete", and saying that out loud
        // is a different decision from setting a number.
        RuleFor(x => x.RecordRetentionDays).InclusiveBetween(1, 7300);
        RuleFor(x => x.AuditRetentionDays).InclusiveBetween(1, 7300);
    }
}
