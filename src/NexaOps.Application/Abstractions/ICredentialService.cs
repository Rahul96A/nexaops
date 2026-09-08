using NexaOps.Domain.Identity;

namespace NexaOps.Application.Abstractions;

/// <summary>
/// Issues and hashes local credentials.
/// <para>
/// An abstraction rather than a direct dependency on the hasher, so the application layer can
/// create a user without knowing how passwords are stored, and so that under federated
/// authentication it can ask whether NexaOps issues credentials at all rather than assuming it
/// does. Creating an account with a local password in an Entra tenant would produce a second,
/// unmanaged way in.
/// </para>
/// </summary>
public interface ICredentialService
{
    /// <summary>False when the tenant authenticates through an external identity provider.</summary>
    bool IssuesLocalCredentials { get; }

    /// <summary>
    /// A single-use password for a new account.
    /// <para>
    /// Generated from a cryptographic source and returned to the caller exactly once, in the
    /// response to the request that created the account. It is never stored in the clear and
    /// cannot be retrieved afterwards — a password an administrator can look up later is a
    /// credential with two owners.
    /// </para>
    /// </summary>
    string GenerateTemporaryPassword();

    string Hash(User user, string password);
}
