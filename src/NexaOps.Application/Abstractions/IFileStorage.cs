namespace NexaOps.Application.Abstractions;

/// <summary>
/// Blob storage port. Azure Blob Storage in Azure, local disk for development.
/// <para>
/// Callers supply a logical path; the implementation prefixes it with the tenant id, so a
/// crafted file name cannot escape its tenant's prefix.
/// </para>
/// </summary>
public interface IFileStorage
{
    Task<StoredFile> UploadAsync(
        string container,
        string logicalPath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(string container, string path, CancellationToken cancellationToken = default);

    Task DeleteAsync(string container, string path, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string container, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// A short-lived, read-only download URL when the provider supports one (an Azure user
    /// delegation SAS). Returns null when it does not, in which case the API streams the bytes
    /// itself. Callers must handle null rather than assuming a URL is always available.
    /// </summary>
    Task<Uri?> TryCreateReadUrlAsync(
        string container,
        string path,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);
}

/// <summary>The outcome of an upload.</summary>
/// <param name="Container">Storage container the file landed in.</param>
/// <param name="Path">Full storage path, tenant-prefixed.</param>
/// <param name="SizeBytes">Bytes written.</param>
/// <param name="ContentHash">Hex SHA-256 of the content.</param>
public sealed record StoredFile(string Container, string Path, long SizeBytes, string ContentHash);
