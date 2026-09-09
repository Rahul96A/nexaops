using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Security;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;
using NexaOps.Domain.Integration;

namespace NexaOps.Application.Integration;

/// <summary>Issuing and revoking machine credentials.</summary>
public interface IIntegrationKeyService
{
    Task<IReadOnlyList<IntegrationKeyDto>> GetKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Issues a key. The secret is in the response and nowhere else, ever again.
    /// </summary>
    Task<IssuedIntegrationKeyDto> IssueAsync(IssueIntegrationKeyCommand command, CancellationToken ct = default);

    Task RevokeAsync(Guid id, CancellationToken ct = default);
}

public sealed record IntegrationKeyDto(
    Guid Id,
    string Name,
    string? Description,
    string Prefix,
    IntegrationScope Scope,
    Guid ServiceAccountUserId,
    string? ServiceAccountName,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    bool IsUsable,
    DateTimeOffset CreatedAt);

/// <param name="Key">
/// The full secret. Returned in this response only: it is stored as a hash and cannot be
/// recovered, so a key nobody wrote down has to be replaced.
/// </param>
public sealed record IssuedIntegrationKeyDto(IntegrationKeyDto Details, string Key);

public sealed class IssueIntegrationKeyCommand
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IntegrationScope Scope { get; set; } = IntegrationScope.InboundEmail;

    /// <summary>The service account whose roles the key will act with.</summary>
    public Guid ServiceAccountUserId { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <inheritdoc />
public sealed class IntegrationKeyService : IIntegrationKeyService
{
    private const string EntityType = nameof(IntegrationKey);

    /// <summary>
    /// Marks the value as a NexaOps key wherever it turns up.
    /// <para>
    /// A recognisable prefix is what lets secret scanners — the ones that watch public
    /// repositories — spot a leaked key and tell somebody. An opaque blob leaks just as easily
    /// and nobody notices.
    /// </para>
    /// </summary>
    private const string KeyPrefix = "nxk_";

    /// <summary>256 bits of randomness, base64url-encoded.</summary>
    private const int KeyBytes = 32;

    private readonly IIntegrationKeyRepository _keys;
    private readonly IIntegrationDirectory _directory;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<IntegrationKeyService> _logger;

    public IntegrationKeyService(
        IIntegrationKeyRepository keys,
        IIntegrationDirectory directory,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<IntegrationKeyService> logger)
    {
        _keys = keys;
        _directory = directory;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// The hash a key is stored and looked up under.
    /// <para>
    /// Shared with the authentication handler so there is exactly one definition. Two would
    /// eventually disagree, and the failure would be every integration losing access at once.
    /// </para>
    /// </summary>
    public static string Hash(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key ?? string.Empty)));

    /// <inheritdoc />
    public async Task<IReadOnlyList<IntegrationKeyDto>> GetKeysAsync(CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.IntegrationManage);

        var keys = await _keys.GetForTenantAsync(ct).ConfigureAwait(false);
        var now = _clock.UtcNow;

        return
        [
            .. keys.Select(k => new IntegrationKeyDto(
                k.Id,
                k.Name,
                k.Description,
                k.Prefix,
                k.Scope,
                k.ServiceAccountUserId,
                k.ServiceAccount?.DisplayName,
                k.ExpiresAt,
                k.LastUsedAt,
                k.RevokedAt,
                k.IsUsableAt(now),
                k.CreatedAt))
        ];
    }

    /// <inheritdoc />
    public async Task<IssuedIntegrationKeyDto> IssueAsync(
        IssueIntegrationKeyCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.IntegrationManage);

        var failures = new List<FluentValidation.Results.ValidationFailure>();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            failures.Add(new(nameof(command.Name), "A key needs a name, so somebody can tell later what it is for."));
        }

        if (command.ExpiresAt is not null && command.ExpiresAt <= _clock.UtcNow)
        {
            failures.Add(new(nameof(command.ExpiresAt), "An expiry in the past would issue a key that never works."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // The account must exist in this tenant. A key pointing at nobody would authenticate to
        // a caller with no identity, and every audit entry it produced would be unattributable.
        var account = await _directory
            .FindActiveUserByIdAsync(command.ServiceAccountUserId, ct)
            .ConfigureAwait(false)
            ?? throw new DomainException(
                "integration.unknown_service_account",
                "That service account does not exist or is not active.");

        var secret = KeyPrefix + Base64Url(RandomNumberGenerator.GetBytes(KeyBytes));

        var key = new IntegrationKey
        {
            Name = command.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),

            // Enough to identify the key in a list and in a log line, not enough to use.
            Prefix = secret[..12],
            KeyHash = Hash(secret),
            ServiceAccountUserId = account.Id,
            Scope = command.Scope,
            ExpiresAt = command.ExpiresAt
        };

        _keys.Add(key);

        // The audit records that a key was issued, by whom, and for which account. It does not
        // record the key, which is the one place a hashed secret would leak back into the clear.
        _audit.Record(
            AuditAction.SecurityEvent,
            EntityType,
            key.Id.ToString(),
            key.Name,
            $"Issued the integration key \"{key.Name}\" ({key.Prefix}…) for {account.DisplayName}, "
            + $"scope {key.Scope}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Integration key {KeyId} ({Prefix}) issued by {Actor}.", key.Id, key.Prefix, _currentUser.UserId);

        var details = new IntegrationKeyDto(
            key.Id, key.Name, key.Description, key.Prefix, key.Scope,
            account.Id, account.DisplayName, key.ExpiresAt, null, null, true, _clock.UtcNow);

        return new IssuedIntegrationKeyDto(details, secret);
    }

    /// <inheritdoc />
    public async Task RevokeAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.IntegrationManage);

        var key = await _keys.GetAsync(id, ct).ConfigureAwait(false)
                  ?? throw new EntityNotFoundException(EntityType, id);

        if (key.RevokedAt is not null)
        {
            return;
        }

        // Revoked, never deleted. The key's identifier appears in audit entries for everything it
        // did, and deleting the row would leave those entries pointing at nothing.
        key.RevokedAt = _clock.UtcNow;
        key.RevokedByUserId = _currentUser.UserIdOrNull;

        _audit.Record(
            AuditAction.SecurityEvent,
            EntityType,
            key.Id.ToString(),
            key.Name,
            $"Revoked the integration key \"{key.Name}\" ({key.Prefix}…).");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogWarning(
            "Integration key {KeyId} ({Prefix}) revoked by {Actor}.", key.Id, key.Prefix, _currentUser.UserId);
    }

    /// <summary>URL-safe base64 without padding, so the key survives a query string or a header.</summary>
    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
