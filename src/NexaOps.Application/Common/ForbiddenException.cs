namespace NexaOps.Application.Common;

/// <summary>
/// The caller is authenticated but lacks the permission required for this operation.
/// The API translates this to HTTP 403 and writes an <c>AccessDenied</c> audit event; the
/// denied permission code is recorded server-side but not echoed to the caller.
/// </summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string permission)
        : base($"The caller does not hold the required permission '{permission}'.")
        => Permission = permission;

    public ForbiddenException(string permission, string message) : base(message)
        => Permission = permission;

    public string Permission { get; }
}

/// <summary>
/// A request that is well-formed but conflicts with current state - a stale row version, or a
/// duplicate of something that must be unique. Translated to HTTP 409.
/// </summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}
