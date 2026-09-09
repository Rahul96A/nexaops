using NexaOps.Domain.Common;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Integration;

/// <summary>
/// One email that arrived, and what became of it.
/// <para>
/// Recorded whatever the outcome, including the ones that were ignored. "I emailed the service
/// desk and nothing happened" is the question this module gets asked, and it cannot be answered
/// from a log of successes — the same reason workflow runs record their skips.
/// </para>
/// </summary>
public sealed class InboundMessage : TenantEntity
{
    /// <summary>
    /// The RFC 5322 Message-ID, as the sending client wrote it.
    /// <para>
    /// The duplicate key. Mail providers retry deliveries, and a retry that raised a second
    /// ticket would be the most visible possible failure of this feature.
    /// </para>
    /// </summary>
    public required string ExternalMessageId { get; set; }

    /// <summary>The Message-ID this is a reply to, when the client set one. The strongest thread signal.</summary>
    public string? InReplyTo { get; set; }

    public required string FromAddress { get; set; }

    public string? FromDisplayName { get; set; }

    public required string Subject { get; set; }

    /// <summary>
    /// The opening of the body, kept for diagnosis.
    /// <para>
    /// A preview rather than the whole message: the body already lives on the record it created,
    /// and storing a second full copy here would double the retention question for no gain.
    /// </para>
    /// </summary>
    public string? BodyPreview { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public InboundMessageStatus Status { get; set; }

    /// <summary>The record this became, or was appended to.</summary>
    public Guid? RecordId { get; set; }

    public string? RecordNumber { get; set; }

    /// <summary>Why it was ignored or failed, in a sentence an administrator can act on.</summary>
    public string? Outcome { get; set; }
}

/// <summary>What happened to a message that arrived.</summary>
public enum InboundMessageStatus
{
    /// <summary>A new record was raised from it.</summary>
    RecordCreated = 1,

    /// <summary>It was recognised as a reply and added to an existing record.</summary>
    AppendedToRecord = 2,

    /// <summary>Already seen. The provider retried; nothing was created twice.</summary>
    Duplicate = 3,

    /// <summary>
    /// Deliberately not acted on — an unknown sender, an automated bounce, an empty subject.
    /// Recorded rather than dropped so somebody can see why their email went nowhere.
    /// </summary>
    Ignored = 4,

    /// <summary>Something went wrong. The message is kept so it can be looked at.</summary>
    Failed = 5
}

/// <summary>
/// Decides whether an arriving email belongs to a record that already exists.
/// <para>
/// Pure, and the part of this module most worth testing: getting it wrong in one direction
/// scatters a conversation across several tickets, and in the other it appends somebody's new
/// problem to an unrelated old one.
/// </para>
/// </summary>
public static class EmailThreadMatcher
{
    /// <summary>
    /// Reply and forward prefixes, in the languages a support mailbox in India actually receives.
    /// <para>
    /// Stripped so that "Re: Re: FW: Printer jam" threads with "Printer jam". Matching on the raw
    /// subject would make every reply look like a new problem.
    /// </para>
    /// </summary>
    private static readonly string[] Prefixes =
        ["re:", "re :", "fw:", "fwd:", "aw:", "antwort:", "ref:", "उत्तर:"];

    /// <summary>
    /// Pulls a record number out of a subject line.
    /// <para>
    /// Only from a bracketed tag that NexaOps itself put there — <c>[INC0001234]</c>. A bare
    /// number anywhere in the text is not trusted: people paste ticket numbers into unrelated
    /// emails all the time, and an email that mentions a ticket is not necessarily about it.
    /// </para>
    /// <para>
    /// The number found here is a candidate only. The caller must confirm the record exists in
    /// this tenant before using it, which is what stops somebody threading their way into
    /// another tenant's ticket by guessing at a subject line.
    /// </para>
    /// </summary>
    public static string? ExtractRecordNumber(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var open = subject.IndexOf('[', StringComparison.Ordinal);

        while (open >= 0)
        {
            var close = subject.IndexOf(']', open + 1);

            if (close < 0)
            {
                return null;
            }

            var candidate = subject[(open + 1)..close].Trim();

            if (LooksLikeRecordNumber(candidate))
            {
                return candidate.ToUpperInvariant();
            }

            open = subject.IndexOf('[', close + 1);
        }

        return null;
    }

    /// <summary>
    /// A subject with reply and forward prefixes removed, for comparing two subjects.
    /// <para>
    /// Repeated because a long thread accumulates them: "Re: Fwd: Re: ..." is ordinary.
    /// </para>
    /// </summary>
    public static string NormaliseSubject(string? subject)
    {
        var text = (subject ?? string.Empty).Trim();

        bool stripped;

        do
        {
            stripped = false;

            foreach (var prefix in Prefixes)
            {
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    text = text[prefix.Length..].TrimStart();
                    stripped = true;
                    break;
                }
            }
        }
        while (stripped && text.Length > 0);

        // The tag NexaOps adds is threading metadata, not part of what the person wrote.
        var number = ExtractRecordNumber(text);

        if (number is not null)
        {
            text = text.Replace($"[{number}]", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        }

        return text;
    }

    /// <summary>
    /// Whether a subject looks like a reply rather than a fresh message.
    /// <para>
    /// Used only to decide how hard to look for a thread. A reply whose record cannot be found
    /// still becomes a new ticket — refusing it would lose somebody's message entirely, which is
    /// worse than an extra ticket somebody can merge.
    /// </para>
    /// </summary>
    public static bool LooksLikeReply(string? subject)
    {
        var text = (subject ?? string.Empty).TrimStart();

        return Prefixes.Any(p => text.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A record number is a module prefix and digits — INC0001234, REQ0000042.
    /// <para>
    /// Checked structurally rather than against a list of prefixes, so a module added later
    /// threads without anybody remembering to come back here.
    /// </para>
    /// </summary>
    private static bool LooksLikeRecordNumber(string candidate)
    {
        if (candidate.Length is < 4 or > 32)
        {
            return false;
        }

        var letters = 0;

        while (letters < candidate.Length && char.IsLetter(candidate[letters]))
        {
            letters++;
        }

        if (letters is < 2 or > 8 || letters == candidate.Length)
        {
            return false;
        }

        return candidate[letters..].All(char.IsAsciiDigit);
    }
}
