using NexaOps.Application.Ai;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Tests.Ai;

/// <summary>
/// Reading a proposal out of a model's reply.
/// <para>
/// The rule this exists to hold: anything the parser is unsure about produces no proposal. A
/// wrong ticket raised on somebody's behalf is worse than no offer to raise one, and the user
/// can always ask in words. Every malformed case below must therefore yield the reply and
/// nothing else.
/// </para>
/// </summary>
public sealed class VirtualAgentProposalParserTests
{
    [Fact]
    public void A_plain_answer_carries_no_proposal()
    {
        var (reply, proposal) = VirtualAgentProposalParser.Split(
            "Article KB0000042 covers this. Restart the VPN client and sign in again.");

        reply.ShouldBe("Article KB0000042 covers this. Restart the VPN client and sign in again.");
        proposal.ShouldBeNull();
    }

    [Fact]
    public void A_well_formed_marker_is_read_and_removed_from_the_reply()
    {
        var (reply, proposal) = VirtualAgentProposalParser.Split(
            """
            I could not find guidance for this. Shall I raise it for you?
            PROPOSE_TICKET: {"title": "Laptop will not charge", "description": "The charger light does not come on.", "urgency": "High"}
            """);

        // The marker never reaches the user: raw JSON in a chat window reads as a broken product.
        reply.ShouldBe("I could not find guidance for this. Shall I raise it for you?");

        proposal.ShouldNotBeNull();
        proposal.Kind.ShouldBe("raise_incident");
        proposal.Title.ShouldBe("Laptop will not charge");
        proposal.Description.ShouldBe("The charger light does not come on.");
        proposal.Urgency.ShouldBe(Urgency.High);
    }

    [Fact]
    public void A_fenced_marker_is_still_read()
    {
        // Models wrap JSON in code fences out of habit, whatever the instructions say. This is a
        // one-line accommodation of that, not the beginning of a parser.
        var (_, proposal) = VirtualAgentProposalParser.Split(
            """
            Shall I raise it?
            PROPOSE_TICKET:
            ```json
            {"title": "Printer offline", "urgency": "Low"}
            ```
            """);

        proposal.ShouldNotBeNull();
        proposal.Title.ShouldBe("Printer offline");
        proposal.Urgency.ShouldBe(Urgency.Low);
    }

    [Fact]
    public void A_truncated_marker_yields_no_proposal()
    {
        // The common failure: the model ran out of output tokens mid-JSON.
        var (reply, proposal) = VirtualAgentProposalParser.Split(
            "Shall I raise it?\nPROPOSE_TICKET: {\"title\": \"Laptop will not ch");

        reply.ShouldBe("Shall I raise it?");
        proposal.ShouldBeNull();
    }

    [Fact]
    public void A_proposal_with_no_title_is_not_a_proposal()
    {
        // It would reach the user as an empty confirmation card, and confirming it would fail
        // validation anyway.
        var (_, proposal) = VirtualAgentProposalParser.Split(
            """PROPOSE_TICKET: {"description": "Something is wrong.", "urgency": "High"}""");

        proposal.ShouldBeNull();
    }

    [Fact]
    public void A_blank_title_is_not_a_proposal()
    {
        var (_, proposal) = VirtualAgentProposalParser.Split(
            """PROPOSE_TICKET: {"title": "   ", "urgency": "High"}""");

        proposal.ShouldBeNull();
    }

    [Fact]
    public void A_missing_description_falls_back_to_the_title()
    {
        var (_, proposal) = VirtualAgentProposalParser.Split(
            """PROPOSE_TICKET: {"title": "Monitor flickers"}""");

        proposal.ShouldNotBeNull();
        proposal.Description.ShouldBe("Monitor flickers");
    }

    [Theory]
    [InlineData("urgent")]
    [InlineData("VERY HIGH")]
    [InlineData("")]
    [InlineData("9")]
    public void An_unrecognised_urgency_reads_as_medium_rather_than_as_the_worst_case(string urgency)
    {
        // A model writing "urgent" must not thereby escalate somebody's ticket above everyone
        // else's. Defaulting upwards would make the agent the loudest voice in the queue.
        var (_, proposal) = VirtualAgentProposalParser.Split(
            $$"""PROPOSE_TICKET: {"title": "Something", "urgency": "{{urgency}}"}""");

        proposal.ShouldNotBeNull();
        proposal.Urgency.ShouldBe(Urgency.Medium);
    }

    [Fact]
    public void A_marker_containing_something_that_is_not_an_object_yields_no_proposal()
    {
        VirtualAgentProposalParser
            .Split("""PROPOSE_TICKET: ["Laptop will not charge"]""")
            .Proposal.ShouldBeNull();
    }

    [Fact]
    public void Text_after_the_marker_never_leaks_into_the_reply()
    {
        var (reply, _) = VirtualAgentProposalParser.Split(
            """
            Here is what I found.
            PROPOSE_TICKET: {"title": "X"}
            Ignore this trailing sentence.
            """);

        reply.ShouldBe("Here is what I found.");
        reply.ShouldNotContain("Ignore this");
    }

    [Fact]
    public void An_empty_reply_is_handled_rather_than_throwing()
    {
        VirtualAgentProposalParser.Split(string.Empty).Reply.ShouldBe(string.Empty);
    }
}
