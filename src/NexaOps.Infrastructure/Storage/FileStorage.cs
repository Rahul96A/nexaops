using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaOps.Application.Abstractions;

namespace NexaOps.Infrastructure.Storage;

/// <summary>Blob storage configuration. Prefers Managed Identity over any connection string.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Storage account blob endpoint, e.g. <c>https://stnexaopsprod.blob.core.windows.net</c>.
    /// Used with Managed Identity. This is the deployed configuration.
    /// </summary>
    public string? BlobServiceUri { get; set; }

    /// <summary>
    /// Connection string. Supported only for Azurite in local development; production access is
    /// via Managed Identity so that no storage key exists to leak.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Root folder used when neither of the above is set, for offline development.</summary>
    public string LocalRootPath { get; set; } = "App_Data/storage";

    /// <summary>Hard ceiling on any single upload.</summary>
    public long MaxUploadBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>
    /// Extensions accepted for upload. An allow-list rather than a block-list: a block-list is
    /// always one new extension behind.
    /// </summary>
    public string[] AllowedExtensions { get; set; } =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
        ".pdf", ".txt", ".csv", ".log", ".json", ".xml",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".zip", ".msg", ".eml"
    ];
}

/// <summary>
/// Azure Blob Storage adapter with a local-filesystem fallback so the product runs end to end
/// on a laptop with no Azure subscription.
/// </summary>
public sealed class BlobFileStorage : IFileStorage
{
    private readonly BlobServiceClient? _client;
    private readonly StorageOptions _options;
    private readonly ITenantContext _tenant;
    private readonly ILogger<BlobFileStorage> _logger;

    public BlobFileStorage(
        BlobServiceClient? client,
        IOptions<StorageOptions> options,
        ITenantContext tenant,
        ILogger<BlobFileStorage> logger)
    {
        _client = client;
        _options = options.Value;
        _tenant = tenant;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StoredFile> UploadAsync(
        string container,
        string logicalPath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = ScopedPath(logicalPath);

        // Hash while buffering so integrity and de-duplication come free, and so the size is
        // known before anything is written.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        if (buffer.Length > _options.MaxUploadBytes)
        {
            throw new InvalidOperationException(
                $"The file is {buffer.Length} bytes, which exceeds the {_options.MaxUploadBytes} byte limit.");
        }

        var hash = Convert.ToHexString(await SHA256.HashDataAsync(buffer, cancellationToken)
            .ConfigureAwait(false)).ToLowerInvariant();
        buffer.Position = 0;

        if (_client is null)
        {
            await WriteLocalAsync(container, path, buffer, cancellationToken).ConfigureAwait(false);
            return new StoredFile(container, path, buffer.Length, hash);
        }

        var containerClient = _client.GetBlobContainerClient(container);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var blob = containerClient.GetBlobClient(path);

        await blob.UploadAsync(
            buffer,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType,

                    // Force a download rather than inline rendering. An HTML or SVG attachment
                    // rendered inline on the storage origin would be stored cross-site scripting.
                    ContentDisposition = "attachment"
                }
            },
            cancellationToken).ConfigureAwait(false);

        return new StoredFile(container, path, buffer.Length, hash);
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(
        string container,
        string path,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantScoped(path);

        if (_client is null)
        {
            var local = LocalPath(container, path);
            return File.Exists(local) ? File.OpenRead(local) : null;
        }

        try
        {
            var blob = _client.GetBlobContainerClient(container).GetBlobClient(path);
            var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string container, string path, CancellationToken cancellationToken = default)
    {
        EnsureTenantScoped(path);

        if (_client is null)
        {
            var local = LocalPath(container, path);
            if (File.Exists(local))
            {
                File.Delete(local);
            }

            return;
        }

        await _client.GetBlobContainerClient(container)
            .GetBlobClient(path)
            .DeleteIfExistsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string container,
        string path,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantScoped(path);

        if (_client is null)
        {
            return File.Exists(LocalPath(container, path));
        }

        var blob = _client.GetBlobContainerClient(container).GetBlobClient(path);
        return await blob.ExistsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Uri?> TryCreateReadUrlAsync(
        string container,
        string path,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantScoped(path);

        if (_client is null)
        {
            return null;
        }

        var blob = _client.GetBlobContainerClient(container).GetBlobClient(path);

        // A shared key SAS would require a storage account key, which we deliberately do not
        // hold. Under Managed Identity a user delegation SAS is the correct mechanism; when it
        // is unavailable we return null and the API streams the bytes itself.
        if (!blob.CanGenerateSasUri)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;

                var delegationKey = await _client
                    .GetUserDelegationKeyAsync(now.AddMinutes(-5), now.Add(lifetime), cancellationToken)
                    .ConfigureAwait(false);

                var builder = new BlobSasBuilder(BlobSasPermissions.Read, now.Add(lifetime))
                {
                    BlobContainerName = container,
                    BlobName = path,
                    Resource = "b",
                    StartsOn = now.AddMinutes(-5)
                };

                var sas = builder.ToSasQueryParameters(delegationKey.Value, _client.AccountName).ToString();
                return new Uri($"{blob.Uri}?{sas}");
            }
            catch (RequestFailedException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not create a user delegation SAS; the API will stream the file instead.");
                return null;
            }
        }

        return blob.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(lifetime));
    }

    /// <summary>
    /// Prefixes a caller-supplied logical path with the ambient tenant and strips any traversal
    /// attempt, so a crafted file name cannot reach another tenant's blobs.
    /// </summary>
    private string ScopedPath(string logicalPath)
    {
        var cleaned = logicalPath
            .Replace('\\', '/')
            .Replace("..", string.Empty, StringComparison.Ordinal)
            .TrimStart('/');

        return $"{_tenant.TenantId:N}/{cleaned}";
    }

    /// <summary>
    /// Confirms a stored path belongs to the ambient tenant before it is read or deleted.
    /// The path comes from our own database, but checking it here means a compromised or
    /// mistaken row still cannot reach across a tenant boundary.
    /// </summary>
    private void EnsureTenantScoped(string path)
    {
        var expected = $"{_tenant.TenantId:N}/";

        if (!path.StartsWith(expected, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The requested file does not belong to the current tenant.");
        }
    }

    private string LocalPath(string container, string path)
        => Path.Combine(_options.LocalRootPath, container, path.Replace('/', Path.DirectorySeparatorChar));

    private async Task WriteLocalAsync(
        string container,
        string path,
        Stream content,
        CancellationToken cancellationToken)
    {
        var target = LocalPath(container, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        await using var file = File.Create(target);
        await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Stored {Path} on the local filesystem (no blob storage configured).", target);
    }
}
