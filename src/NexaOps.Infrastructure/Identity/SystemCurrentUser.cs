using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Security;

namespace NexaOps.Infrastructure.Identity;

/// <summary>
/// The identity used by background workers, the demo seeder, and migrations - work that is not
/// performed on behalf of a signed-in person.
/// <para>
/// It reports itself as unauthenticated so that audit rows say "System" rather than
/// impersonating a real user, but it holds every permission because it runs trusted code paths
/// that were never reached through an HTTP request.
/// </para>
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    /// <summary>A stable, well-known id so system-originated audit rows are attributable.</summary>
    public static readonly Guid SystemUserId = new("00000000-0000-0000-0000-00000000515e");

    /// <inheritdoc />
    public bool IsAuthenticated => false;

    /// <inheritdoc />
    public Guid UserId => SystemUserId;

    /// <inheritdoc />
    public Guid? UserIdOrNull => null;

    /// <inheritdoc />
    public string Email => "system@nexaops.local";

    /// <inheritdoc />
    public string DisplayName => "System";

    /// <inheritdoc />
    public IReadOnlySet<string> Permissions { get; } =
        NexaOps.Application.Security.Permissions.All.ToHashSet(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlySet<Guid> GroupIds { get; } = new HashSet<Guid>();

    /// <inheritdoc />
    public bool IsPlatformAdministrator => true;

    /// <inheritdoc />
    public bool HasPermission(string permission) => true;

    /// <inheritdoc />
    public void DemandPermission(string permission)
    {
        // Trusted internal code path; nothing to enforce.
    }
}
