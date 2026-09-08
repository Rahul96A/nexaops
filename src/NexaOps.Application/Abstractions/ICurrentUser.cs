namespace NexaOps.Application.Abstractions;

/// <summary>
/// The authenticated caller. Backed by the validated security token, never by request input.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The NexaOps user id. Throws when unauthenticated.</summary>
    Guid UserId { get; }

    /// <summary>Null when unauthenticated, so callers that tolerate anonymity need not catch.</summary>
    Guid? UserIdOrNull { get; }

    string Email { get; }
    string DisplayName { get; }

    /// <summary>Effective permission codes, expanded from every non-expired role assignment.</summary>
    IReadOnlySet<string> Permissions { get; }

    /// <summary>Assignment groups the caller belongs to. Used for "my team's queue" filtering.</summary>
    IReadOnlySet<Guid> GroupIds { get; }

    /// <summary>True when the caller holds a platform-scoped role.</summary>
    bool IsPlatformAdministrator { get; }

    bool HasPermission(string permission);

    /// <summary>Throws <see cref="Common.ForbiddenException"/> when the permission is absent.</summary>
    void DemandPermission(string permission);
}
