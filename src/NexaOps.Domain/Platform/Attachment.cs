using NexaOps.Domain.Common;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Platform;

/// <summary>
/// Metadata for a file held in blob storage. The bytes never go into SQL - the row records
/// where they are, what they are, and who put them there.
/// </summary>
public class Attachment : TenantEntity
{
    public ServiceModule Module { get; set; }

    /// <summary>Identifier of the record the file hangs off.</summary>
    public Guid RecordId { get; set; }

    /// <summary>Original client-supplied name, sanitised. Never used to build a storage path.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Server-detected content type, not the client-declared one.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    public string BlobContainer { get; set; } = string.Empty;

    /// <summary>
    /// Storage path. Always generated server-side and always tenant-prefixed, so a crafted
    /// file name cannot traverse into another tenant's blobs.
    /// </summary>
    public string BlobPath { get; set; } = string.Empty;

    /// <summary>Hex SHA-256 of the content, for de-duplication and integrity checks.</summary>
    public string? ContentHash { get; set; }

    public Guid UploadedByUserId { get; set; }

    /// <summary>
    /// Malware scan state. Files are quarantined until scanned, and a file that is not
    /// <see cref="AttachmentScanStatus.Clean"/> is never served to a browser.
    /// </summary>
    public AttachmentScanStatus ScanStatus { get; set; } = AttachmentScanStatus.Pending;
    public DateTimeOffset? ScannedAt { get; set; }

    /// <summary>Internal work notes may carry attachments that the requester must not see.</summary>
    public bool IsInternal { get; set; }
}

public enum AttachmentScanStatus
{
    Pending = 1,
    Clean = 2,
    Infected = 3,
    Failed = 4,

    /// <summary>Scanning is not configured in this environment; the file is held, not served.</summary>
    NotScanned = 5
}
