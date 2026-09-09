using NexaOps.Domain.Integration;

namespace NexaOps.Domain.Tests.Integration;

/// <summary>
/// Deciding whether an arriving email belongs to a ticket that already exists.
/// <para>
/// Wrong in one direction, a conversation scatters across five tickets nobody can follow. Wrong
/// in the other, somebody's new problem is appended to an unrelated closed one and never looked
/// at. Both are worse than the feature not existing, which is why this is pure and tested hard.
/// </para>
/// </summary>
public sealed class EmailThreadMatcherTests
{
    [Theory]
    [InlineData("[INC0001234] Printer jam", "INC0001234")]
    [InlineData("Re: [INC0001234] Printer jam", "INC0001234")]
    [InlineData("FW: [REQ0000042] New laptop", "REQ0000042")]
    [InlineData("Something [inc0001234] lowercase", "INC0001234")]
    public void A_bracketed_record_number_is_found(string subject, string expected)
    {
        EmailThreadMatcher.ExtractRecordNumber(subject).ShouldBe(expected);
    }

    [Theory]
    [InlineData("INC0001234 printer jam")]
    [InlineData("About ticket INC0001234")]
    [InlineData("Printer jam")]
    [InlineData("")]
    [InlineData(null)]
    public void A_bare_number_in_the_text_is_not_trusted(string? subject)
    {
        // People paste ticket numbers into unrelated emails constantly — "this is like
        // INC0001234 last month". An email that mentions a ticket is not an email about it, and
        // appending to the wrong one is worse than raising a new one.
        EmailThreadMatcher.ExtractRecordNumber(subject).ShouldBeNull();
    }

    [Theory]
    [InlineData("[not a number] Printer jam")]
    [InlineData("[12345] Printer jam")]
    [InlineData("[INCIDENT] Printer jam")]
    [InlineData("[INC-0001234] Printer jam")]
    [InlineData("[unclosed INC0001234 Printer jam")]
    public void Something_bracketed_that_is_not_a_record_number_is_ignored(string subject)
    {
        EmailThreadMatcher.ExtractRecordNumber(subject).ShouldBeNull();
    }

    [Fact]
    public void A_later_bracket_is_still_searched()
    {
        // Mailing lists prepend their own tag. The record number is often not the first bracket.
        EmailThreadMatcher
            .ExtractRecordNumber("[service-desk] [INC0001234] Printer jam")
            .ShouldBe("INC0001234");
    }

    [Theory]
    [InlineData("Re: Printer jam", "Printer jam")]
    [InlineData("RE: Printer jam", "Printer jam")]
    [InlineData("Re : Printer jam", "Printer jam")]
    [InlineData("Fwd: Printer jam", "Printer jam")]
    [InlineData("FW: Printer jam", "Printer jam")]
    [InlineData("AW: Printer jam", "Printer jam")]
    [InlineData("Re: Re: FW: Printer jam", "Printer jam")]
    [InlineData("Printer jam", "Printer jam")]
    public void Reply_and_forward_prefixes_are_stripped(string subject, string expected)
    {
        // A long thread accumulates them. Matching on the raw subject would make every reply
        // look like a new problem.
        EmailThreadMatcher.NormaliseSubject(subject).ShouldBe(expected);
    }

    [Fact]
    public void The_record_tag_is_stripped_from_a_normalised_subject()
    {
        // The tag is threading metadata this system added, not part of what the person wrote.
        EmailThreadMatcher
            .NormaliseSubject("Re: [INC0001234] Printer jam")
            .ShouldBe("Printer jam");
    }

    [Fact]
    public void A_subject_that_is_only_prefixes_normalises_to_nothing_rather_than_looping()
    {
        // "Re: Re: Re:" with no subject is a real thing people send. The loop has to terminate.
        EmailThreadMatcher.NormaliseSubject("Re: Re: Re:").ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData("Re: Printer jam", true)]
    [InlineData("fwd: Printer jam", true)]
    [InlineData("Printer jam", false)]
    [InlineData("Rebooting the server", false)]
    [InlineData("", false)]
    public void A_reply_is_recognised_without_being_confused_by_a_word_starting_with_re(
        string subject,
        bool expected)
    {
        // "Rebooting" starts with "re" but is not "Re:". Matching without the colon would treat
        // half the service desk's mail as replies.
        EmailThreadMatcher.LooksLikeReply(subject).ShouldBe(expected);
    }

    [Fact]
    public void Normalising_is_idempotent()
    {
        // The same subject must normalise to the same string whether it has been through once or
        // twice, or two messages in one thread will not match each other.
        var once = EmailThreadMatcher.NormaliseSubject("Re: [INC0001234] Printer jam");

        EmailThreadMatcher.NormaliseSubject(once).ShouldBe(once);
    }

    [Fact]
    public void A_null_subject_is_handled_rather_than_throwing()
    {
        // Mail arrives with no subject. It must become a ticket, not an exception in the
        // ingestion endpoint.
        EmailThreadMatcher.NormaliseSubject(null).ShouldBe(string.Empty);
        EmailThreadMatcher.LooksLikeReply(null).ShouldBeFalse();
    }
}
