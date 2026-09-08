using Microsoft.AspNetCore.Mvc;
using NexaOps.Api.Authorization;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Notifications;
using NexaOps.Application.Security;

namespace NexaOps.Api.Controllers;

/// <summary>
/// In-product notifications for the signed-in user.
/// <para>
/// There is deliberately no endpoint that reads another user's notifications: the recipient
/// predicate is part of every query rather than a filter applied afterwards.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/notifications")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    /// <summary>The caller's notifications, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<NotificationDto>>> Get(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
        => Ok(await _notifications.GetMineAsync(
            new NotificationQuery { UnreadOnly = unreadOnly, Page = page, PageSize = pageSize },
            cancellationToken));

    /// <summary>Unread count for the notification bell.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(UnreadCountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountDto>> UnreadCount(CancellationToken cancellationToken)
        => Ok(new UnreadCountDto(await _notifications.GetUnreadCountAsync(cancellationToken)));

    /// <summary>Marks one of the caller's own notifications read.</summary>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        await _notifications.MarkReadAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Marks every unread notification for the caller as read.</summary>
    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await _notifications.MarkAllReadAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record UnreadCountDto(int Count);

/// <summary>
/// The tenant audit trail.
/// <para>
/// Reading requires <c>audit.read</c>, which no role holds implicitly. There is no write, update
/// or delete endpoint: the trail is append-only by construction, not by convention.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/audit")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditQueryService _audit;

    public AuditController(IAuditQueryService audit) => _audit = audit;

    /// <summary>Searches the audit trail.</summary>
    [HttpGet]
    [RequiresPermission(Permissions.AuditRead)]
    [ProducesResponseType(typeof(PagedResult<AuditEventDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AuditEventDto>>> Search(
        [FromQuery] AuditQuery query,
        CancellationToken cancellationToken)
        => Ok(await _audit.SearchAsync(query, cancellationToken));

    /// <summary>Full audit history for one record, newest first.</summary>
    [HttpGet("{entityType}/{entityId:guid}")]
    [RequiresPermission(Permissions.AuditRead)]
    [ProducesResponseType(typeof(IReadOnlyList<AuditEventDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AuditEventDto>>> ForRecord(
        string entityType,
        Guid entityId,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
        => Ok(await _audit.GetForRecordAsync(entityType, entityId, limit, cancellationToken));
}
