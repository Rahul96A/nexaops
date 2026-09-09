using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Security;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;
using NexaOps.Domain.Integration;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Integration;

/// <summary>
/// Turns email into service desk work.
/// <para>
/// The oldest integration in ITSM and still the one customers ask for first, because the way
/// most people report a problem is to email somebody about it. What arrives here has already
/// been parsed by a mail provider; this decides whether it is a new problem, a reply to one, or
/// something to leave alone.
/// </para>
/// </summary>
public interface IInboundEmailService
{
    Task<InboundEmailResultDto> ReceiveAsync(InboundEmailCommand command, CancellationToken ct = default);

    /// <summary>What has arrived and what became of it, newest first.</summary>
    Task<PagedResult<InboundMessageDto>> GetMessagesAsync(
        InboundMessageQuery query,
        CancellationToken ct = default);
}

/// <summary>An email as a mail provider parsed it.</summary>
public sealed class InboundEmailCommand
{
    /// <summary>RFC 5322 Message-ID. Required: it is what makes delivery idempotent.</summary>
    public string MessageId { get; set; } = string.Empty;

    public string? InReplyTo { get; set; }

    public string From { get; set; } = string.Empty;

    public string? FromDisplayName { get; set; }

    public string? Subject { get; set; }

    /// <summary>The plain-text body. HTML is converted by the provider before it reaches here.</summary>
    public string? Body { get; set; }

    /// <summary>
    /// Headers the provider extracted, lower-cased. Used to spot automated mail, which must not
    /// become tickets.
    /// </summary>
    public IDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <param name="Status">What happened.</param>
/// <param name="RecordNumber">The record it became or joined, when it became one.</param>
/// <param name="Outcome">Why, when it did not.</param>
public sealed record InboundEmailResultDto(
    InboundMessageStatus Status,
    string? RecordNumber,
    Guid? RecordId,
    string? Outcome);

public sealed record InboundMessageDto(
    Guid Id,
    string ExternalMessageId,
    string FromAddress,
    string? FromDisplayName,
    string Subject,
    string? BodyPreview,
    DateTimeOffset ReceivedAt,
    InboundMessageStatus Status,
    Guid? RecordId,
    string? RecordNumber,
    string? Outcome);

public sealed class InboundMessageQuery : PagedQuery
{
    public InboundMessageStatus? Status { get; set; }
    public string? Search { get; set; }
}

/// <inheritdoc />
public sealed class InboundEmailService : IInboundEmailService
{
    /// <summary>How much of the body is kept on the ingestion record for diagnosis.</summary>
    private const int PreviewLength = 500;

    /// <summary>
    /// Headers that mean the message came from a machine.
    /// <para>
    /// Out-of-office replies, bounce notifications and mailing-list traffic must never become
    /// tickets. Two auto-replies answering each other is the classic mail loop, and a service desk that
    /// raises a ticket for every bounce fills its own queue overnight.
    /// </para>
    /// </summary>
    private static readonly string[] AutomatedHeaders =
        ["auto-submitted", "x-auto-response-suppress", "list-id", "list-unsubscribe", "precedence"];

    private readonly IInboundEmailRepository _messages;
    private readonly IIncidentService _incidents;
    private readonly IIntegrationDirectory _directory;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenant;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<InboundEmailService> _logger;

    public InboundEmailService(
        IInboundEmailRepository messages,
        IIncidentService incidents,
        IIntegrationDirectory directory,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        IDateTimeProvider clock,
        ILogger<InboundEmailService> logger)
    {
        _messages = messages;
        _incidents = incidents;
        _directory = directory;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _tenant = tenant;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InboundEmailResultDto> ReceiveAsync(
        InboundEmailCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.MessageId))
        {
            // Without it there is no way to tell a retry from a new message, and the first
            // provider outage would fill the queue with duplicates.
            throw new DomainException(
                "email.message_id_required",
                "The message has no Message-ID, so delivery cannot be made idempotent.");
        }

        var from = (command.From ?? string.Empty).Trim().ToLowerInvariant();

        // Idempotency first, before anything else is decided. A provider retrying a delivery it
        // already made must reach exactly this branch.
        var existing = await _messages
            .FindByExternalIdAsync(command.MessageId, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _logger.LogInformation(
                "Inbound message {MessageId} already handled as {Status}.",
                command.MessageId,
                existing.Status);

            return new InboundEmailResultDto(
                InboundMessageStatus.Duplicate,
                existing.RecordNumber,
                existing.RecordId,
                "This message has already been delivered.");
        }

        var message = new InboundMessage
        {
            TenantId = _tenant.TenantId,
            ExternalMessageId = command.MessageId.Trim(),
            InReplyTo = Clean(command.InReplyTo),
            FromAddress = from,
            FromDisplayName = Clean(command.FromDisplayName),
            Subject = Clean(command.Subject) ?? "(no subject)",
            BodyPreview = Preview(command.Body),
            ReceivedAt = _clock.UtcNow,
            Status = InboundMessageStatus.Ignored
        };

        _messages.Add(message);

        try
        {
            var outcome = await HandleAsync(command, message, from, ct).ConfigureAwait(false);

            await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

            return outcome;
        }
        catch (Exception ex)
        {
            // The ingestion record is kept even when handling failed, so somebody can see that
            // the message arrived and what went wrong. Losing both the ticket and the evidence
            // that anything was ever sent is the worst outcome available.
            _logger.LogError(ex, "Failed to handle inbound message {MessageId}.", command.MessageId);

            message.Status = InboundMessageStatus.Failed;
            message.Outcome = ex is DomainException domain ? domain.Message : "The message could not be processed.";

            await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

            return new InboundEmailResultDto(InboundMessageStatus.Failed, null, null, message.Outcome);
        }
    }

    private async Task<InboundEmailResultDto> HandleAsync(
        InboundEmailCommand command,
        InboundMessage message,
        string from,
        CancellationToken ct)
    {
        if (IsAutomated(command))
        {
            return Ignore(
                message,
                "The message is automated — an auto-reply, a bounce or list traffic — so it was "
                + "not turned into a ticket.");
        }

        // The sender has to be somebody in this tenant. An open mailbox that raises a ticket for
        // any address on the internet is a spam target, and the ticket would have no requester
        // anybody could reply to.
        var sender = await _directory.FindActiveUserByEmailAsync(from, ct).ConfigureAwait(false);

        if (sender is null)
        {
            return Ignore(
                message,
                $"No active account in this tenant uses {from}, so there is nobody to raise the "
                + "ticket for.");
        }

        var threaded = await FindThreadAsync(command, ct).ConfigureAwait(false);

        if (threaded is not null)
        {
            await _incidents
                .AddCommentAsync(
                    threaded.Id,
                    new AddIncidentCommentCommand
                    {
                        Body = Body(command),

                        // A customer's reply is customer-visible correspondence, not an internal
                        // note. Filing it as a work note would hide the reply from the person who
                        // sent it, in their own ticket.
                        Kind = IncidentCommentKind.PublicComment
                    },
                    ct)
                .ConfigureAwait(false);

            message.Status = InboundMessageStatus.AppendedToRecord;
            message.RecordId = threaded.Id;
            message.RecordNumber = threaded.Number;
            message.Outcome = $"Added as a reply to {threaded.Number}.";

            _audit.Record(
                AuditAction.Comment,
                nameof(Incident),
                threaded.Id.ToString(),
                threaded.Number,
                $"Reply received by email from {from}.",
                AuditSource.Integration);

            return new InboundEmailResultDto(
                InboundMessageStatus.AppendedToRecord, threaded.Number, threaded.Id, message.Outcome);
        }

        var incident = await _incidents
            .CreateAsync(
                new CreateIncidentCommand
                {
                    Title = Title(command),
                    Description = Body(command),

                    // Neither impact nor urgency can be read out of an email, and guessing at
                    // them from the wording would be a fabricated measurement. The tenant's
                    // matrix gives the default priority; the desk triages as it always has.
                    Impact = Impact.Moderate,
                    Urgency = Urgency.Medium,
                    Channel = IncidentChannel.Email,

                    // Raised for the person who wrote in, not for the service account that
                    // delivered it. The requester is who gets the updates.
                    RequesterId = sender.Id
                },
                ct)
            .ConfigureAwait(false);

        message.Status = InboundMessageStatus.RecordCreated;
        message.RecordId = incident.Id;
        message.RecordNumber = incident.Number;
        message.Outcome = $"Raised {incident.Number}.";

        _audit.Record(
            AuditAction.Create,
            nameof(Incident),
            incident.Id.ToString(),
            incident.Number,
            $"{incident.Number} raised from an email from {from}.",
            AuditSource.Integration);

        _logger.LogInformation("Raised {Number} from an inbound email.", incident.Number);

        return new InboundEmailResultDto(
            InboundMessageStatus.RecordCreated, incident.Number, incident.Id, message.Outcome);
    }

    /// <inheritdoc />
    public Task<PagedResult<InboundMessageDto>> GetMessagesAsync(
        InboundMessageQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Read with the audit permission rather than a new one: this is the record of what
        // reached the system and what it did, which is the same question the audit trail
        // answers.
        return _messages.SearchAsync(query, ct);
    }

    /// <summary>
    /// Finds the record this message is a reply to, or nothing.
    /// <para>
    /// Two signals, in order of how much they can be trusted. The <c>In-Reply-To</c> header is
    /// set by the sending client and is strong evidence. The bracketed number in the subject is
    /// weaker — a person can type anything — so the record is looked up and only used if it
    /// exists in this tenant, which is also what stops a guessed subject line reaching a
    /// neighbour's ticket.
    /// </para>
    /// </summary>
    private async Task<ThreadTarget?> FindThreadAsync(InboundEmailCommand command, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(command.InReplyTo))
        {
            var previous = await _messages
                .FindByExternalIdAsync(command.InReplyTo.Trim(), ct)
                .ConfigureAwait(false);

            if (previous?.RecordId is not null && previous.RecordNumber is not null)
            {
                return new ThreadTarget(previous.RecordId.Value, previous.RecordNumber);
            }
        }

        var number = EmailThreadMatcher.ExtractRecordNumber(command.Subject);

        if (number is null)
        {
            return null;
        }

        // Tenant-filtered, so a number from another tenant reads as non-existent and the message
        // becomes a new ticket here rather than a comment over there.
        var record = await _directory.FindIncidentByNumberAsync(number, ct).ConfigureAwait(false);

        if (record is null)
        {
            return null;
        }

        // A closed ticket is not reopened by a reply. Reopening has its own permission and its
        // own reason field, and an email cannot supply either — so the reply becomes a new
        // ticket, which the desk can link.
        return record.IsOpen ? new ThreadTarget(record.Id, record.Number) : null;
    }

    private static bool IsAutomated(InboundEmailCommand command)
    {
        foreach (var header in AutomatedHeaders)
        {
            if (!command.Headers.TryGetValue(header, out var value))
            {
                continue;
            }

            // "auto-submitted: no" is the explicit statement that a human sent it, and is the
            // one value of these headers that does not mean automation.
            if (header == "auto-submitted"
                && string.Equals(value?.Trim(), "no", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static InboundEmailResultDto Ignore(InboundMessage message, string reason)
    {
        message.Status = InboundMessageStatus.Ignored;
        message.Outcome = reason;

        return new InboundEmailResultDto(InboundMessageStatus.Ignored, null, null, reason);
    }

    /// <summary>
    /// The subject as a ticket title, with threading noise removed.
    /// <para>
    /// An empty subject becomes a stated placeholder rather than an empty title: the ticket has
    /// to be findable in a list, and "(no subject)" tells an agent exactly what happened.
    /// </para>
    /// </summary>
    private static string Title(InboundEmailCommand command)
    {
        var subject = EmailThreadMatcher.NormaliseSubject(command.Subject);

        if (string.IsNullOrWhiteSpace(subject))
        {
            return "Email with no subject";
        }

        return subject.Length <= 200 ? subject : subject[..197].TrimEnd() + "…";
    }

    private static string Body(InboundEmailCommand command)
        => string.IsNullOrWhiteSpace(command.Body)
            ? "The email had no body."
            : command.Body.Trim();

    private static string? Preview(string? body)
        => string.IsNullOrWhiteSpace(body)
            ? null
            : body.Length <= PreviewLength ? body.Trim() : body[..PreviewLength].Trim();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ThreadTarget(Guid Id, string Number);
}
