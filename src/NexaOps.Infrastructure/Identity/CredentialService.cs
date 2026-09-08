using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NexaOps.Application.Abstractions;
using NexaOps.Domain.Identity;

namespace NexaOps.Infrastructure.Identity;

/// <inheritdoc />
public sealed class CredentialService : ICredentialService
{
    /// <summary>
    /// The alphabet a generated password is drawn from.
    /// <para>
    /// Deliberately excludes characters people misread when a password is dictated over the
    /// phone or copied off a screen — no O/0, no l/1/I. A temporary password exists to be typed
    /// once by somebody who was told it, and a character nobody can identify wastes a support
    /// call. The length compensates for the smaller alphabet.
    /// </para>
    /// </summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    private const int PasswordLength = 16;

    private readonly IPasswordHasher<User> _hasher;
    private readonly AuthOptions _options;

    public CredentialService(IPasswordHasher<User> hasher, IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _hasher = hasher;
        _options = options.Value;
    }

    /// <inheritdoc />
    public bool IssuesLocalCredentials => _options.Mode == AuthMode.Local;

    /// <inheritdoc />
    public string GenerateTemporaryPassword()
        // RandomNumberGenerator, not Random: this is a credential, and a predictable one is a
        // credential anybody who knows when the account was created can guess.
        => RandomNumberGenerator.GetString(Alphabet, PasswordLength);

    /// <inheritdoc />
    public string Hash(User user, string password) => _hasher.HashPassword(user, password);
}
