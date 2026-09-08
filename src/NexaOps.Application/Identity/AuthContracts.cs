namespace NexaOps.Application.Identity;

/// <summary>
/// Authentication for the local (first-party JWT) mode.
/// <para>
/// Under Entra ID the token is issued by Microsoft and this service is not used for sign-in;
/// <see cref="GetProfileAsync"/> is still used to project the caller's profile and permissions.
/// </para>
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Verifies credentials and issues a token pair.
    /// <para>
    /// Every failure - unknown email, wrong password, disabled account, locked account - returns
    /// the same generic message, so the endpoint cannot be used to enumerate valid accounts.
    /// The specific reason is written to the audit trail and the server log.
    /// </para>
    /// </summary>
    Task<AuthenticationResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a refresh token for a new pair, rotating the old one.
    /// Presenting a token that has already been rotated revokes the whole session chain.
    /// </summary>
    Task<AuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes the presented refresh token and its session.</summary>
    Task SignOutAsync(string? refreshToken, CancellationToken cancellationToken = default);

    /// <summary>The caller's profile, effective permissions and tenant. Requires authentication.</summary>
    Task<UserProfileDto> GetProfileAsync(CancellationToken cancellationToken = default);

    /// <summary>Changes the caller's own password, revoking every other active session.</summary>
    Task ChangePasswordAsync(
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Credentials plus the tenant hint needed when one email exists in several tenants.</summary>
public sealed class SignInRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Optional tenant code. Required only when the same email address is registered in more
    /// than one tenant, which the sign-in response asks for explicitly.
    /// </summary>
    public string? TenantCode { get; set; }
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>A successful sign-in or refresh.</summary>
/// <param name="AccessToken">Short-lived bearer token.</param>
/// <param name="ExpiresAt">Access token expiry, so the client can refresh proactively.</param>
/// <param name="RefreshToken">Opaque rotating token. Stored only as a hash server-side.</param>
/// <param name="RefreshTokenExpiresAt">Refresh token expiry.</param>
/// <param name="Profile">The signed-in user, ready for the UI without a second round trip.</param>
public sealed record AuthenticationResult(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserProfileDto Profile);

/// <summary>The caller as the UI needs to know them.</summary>
public sealed record UserProfileDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string FirstName,
    string LastName,
    string? JobTitle,
    string? AvatarColor,
    Guid TenantId,
    string TenantCode,
    string TenantName,
    string TimeZoneId,
    string Locale,
    string CurrencyCode,
    string DateFormat,
    Guid? OrganizationId,
    string? OrganizationName,
    Guid? DepartmentId,
    string? DepartmentName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<GroupMembershipDto> Groups,
    bool MustChangePassword,
    bool IsPlatformAdministrator);

/// <param name="GroupId">Group identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="IsLead">Whether the user leads the group.</param>
public sealed record GroupMembershipDto(Guid GroupId, string Name, bool IsLead);

/// <summary>
/// Raised for every failed sign-in. Carries a specific <see cref="Reason"/> for the audit trail
/// and logs, while the API returns a single generic message to the caller.
/// </summary>
public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException(string reason)
        : base("The email address or password is incorrect.")
        => Reason = reason;

    /// <summary>Server-side detail, e.g. <c>account_locked</c>. Never returned to the client.</summary>
    public string Reason { get; }
}

/// <summary>
/// Raised when the email matches users in several tenants and no tenant code was supplied.
/// The client re-submits with a <see cref="SignInRequest.TenantCode"/>.
/// </summary>
public sealed class TenantSelectionRequiredException : Exception
{
    public TenantSelectionRequiredException(IReadOnlyList<TenantChoice> choices)
        : base("Several organizations use this email address. Choose one to continue.")
        => Choices = choices;

    public IReadOnlyList<TenantChoice> Choices { get; }
}

/// <param name="Code">Tenant code to send back in the next sign-in attempt.</param>
/// <param name="Name">Tenant display name.</param>
public sealed record TenantChoice(string Code, string Name);
