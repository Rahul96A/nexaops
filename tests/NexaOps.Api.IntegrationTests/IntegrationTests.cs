using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Administration;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Integration;
using NexaOps.Application.Security;
using NexaOps.Domain.Integration;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// The integration surface end to end, over real HTTP against real SQL Server.
/// <para>
/// The behaviours worth protecting: a key authenticates as an ordinary caller and is bound to
/// one tenant, a retried delivery does not raise a second ticket, a reply joins the conversation
/// it belongs to and not one in a neighbouring tenant, and a message that goes nowhere still
/// leaves evidence that it arrived.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class IntegrationSurfaceTests
{
    private readonly TestEnvironment _env;

    public IntegrationSurfaceTests(TestEnvironment env) => _env = env;

    // -----------------------------------------------------------------
    // Keys
    // -----------------------------------------------------------------

    [Fact]
    public async Task A_key_is_returned_once_and_never_again()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var issued = await IssueKeyAsync(admin, "Shown once");

        issued.Key.ShouldStartWith("nxk_");
        issued.Details.Prefix.ShouldBe(issued.Key[..12]);

        // Listing shows the prefix so a person can tell keys apart, and nothing more. The full
        // value is stored as a hash and cannot be recovered — losing it means issuing another.
        var listed = await admin.GetStringAsync("/api/v1/integration/keys");

        listed.ShouldContain(issued.Details.Prefix);
        listed.ShouldNotContain(issued.Key);
    }

    [Fact]
    public async Task Issuing_a_key_is_not_something_an_agent_can_do()
    {
        // A key acts as a service account without a person behind it, so issuing one is closer
        // to granting a role than to ordinary administration.
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        (await agent.GetAsync("/api/v1/integration/keys")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var response = await agent.PostAsJsonAsync(
            "/api/v1/integration/keys",
            new { name = "Attempt", serviceAccountUserId = _env.Acme.Agent.Id },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_key_pointing_at_nobody_is_refused()
    {
        // It would authenticate a caller with no identity, and every audit entry it produced
        // would be unattributable.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var response = await admin.PostAsJsonAsync(
            "/api/v1/integration/keys",
            new { name = "Orphan", serviceAccountUserId = Guid.NewGuid() },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("integration.unknown_service_account");
    }

    [Fact]
    public async Task An_unknown_key_and_a_revoked_key_fail_the_same_way()
    {
        // Distinguishing them would tell somebody probing which of their guesses was once real.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var issued = await IssueKeyAsync(admin, "To be revoked");

        (await admin.DeleteAsync($"/api/v1/integration/keys/{issued.Details.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var revoked = await DeliverAsync(issued.Key, NewEmail(_env.Acme.Employee.Email));
        var nonsense = await DeliverAsync("nxk_not-a-real-key", NewEmail(_env.Acme.Employee.Email));

        revoked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        nonsense.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_ingestion_endpoint_is_closed_to_a_caller_with_no_key()
    {
        var anonymous = _env.CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/v1/integration/email", NewEmail(_env.Acme.Employee.Email), TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // -----------------------------------------------------------------
    // Ingestion
    // -----------------------------------------------------------------

    [Fact]
    public async Task An_email_becomes_an_incident_raised_for_whoever_sent_it()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);
        var key = (await IssueKeyAsync(admin, "Mailbox")).Key;

        var email = NewEmail(_env.Acme.Employee.Email, subject: "The lift card reader is dead");

        var result = await ReceiveAsync(key, email);

        result.Status.ShouldBe(InboundMessageStatus.RecordCreated);
        result.RecordNumber.ShouldNotBeNull();

        // Raised for the person who wrote in, not for the service account that delivered it:
        // the requester is who gets the updates.
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var incident = await employee.GetFromJsonAsync<IncidentDetailDto>(
            $"/api/v1/incidents/{result.RecordId}", TestEnvironment.Json);

        incident!.Title.ShouldBe("The lift card reader is dead");
        incident.RequesterId.ShouldBe(_env.Acme.Employee.Id);
        incident.Channel.ShouldBe(NexaOps.Domain.ServiceDesk.IncidentChannel.Email);
    }

    [Fact]
    public async Task Delivering_the_same_message_twice_does_not_raise_two_tickets()
    {
        // Mail providers retry. A retry that raised a second ticket would be the most visible
        // possible failure of this feature.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);
        var key = (await IssueKeyAsync(admin, "Retrying provider")).Key;

        var email = NewEmail(_env.Acme.Employee.Email, subject: "Delivered twice");

        var first = await ReceiveAsync(key, email);
        var second = await ReceiveAsync(key, email);

        first.Status.ShouldBe(InboundMessageStatus.RecordCreated);

        second.Status.ShouldBe(InboundMessageStatus.Duplicate);
        second.RecordNumber.ShouldBe(first.RecordNumber);
        second.RecordId.ShouldBe(first.RecordId);
    }

    [Fact]
    public async Task A_reply_joins_the_ticket_it_is_replying_to()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);
        var key = (await IssueKeyAsync(admin, "Threading")).Key;

        var original = NewEmail(_env.Acme.Employee.Email, subject: "My monitor flickers");
        var raised = await ReceiveAsync(key, original);

        var reply = NewEmail(
            _env.Acme.Employee.Email,
            subject: $"Re: {original.Subject}",
            inReplyTo: original.MessageId,
            body: "It has got worse this morning.");

        var appended = await ReceiveAsync(key, reply);

        appended.Status.ShouldBe(InboundMessageStatus.AppendedToRecord);
        appended.RecordId.ShouldBe(raised.RecordId);

        // Filed as customer-visible correspondence, not an internal note: hiding somebody's own
        // reply from them in their own ticket would be absurd.
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var comments = await employee.GetStringAsync($"/api/v1/incidents/{raised.RecordId}/comments");
        comments.ShouldContain("It has got worse this morning.");
    }

    [Fact]
    public async Task A_reply_threads_on_the_ticket_number_in_the_subject()
    {
        // The other threading signal: a person replying from a client that drops In-Reply-To
        // still lands on the right ticket, because the tag NexaOps put in the subject survives.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);
        var key = (await IssueKeyAsync(admin, "Subject threading")).Key;

        var raised = await ReceiveAsync(key, NewEmail(_env.Acme.Employee.Email, subject: "Wifi drops out"));

        var reply = NewEmail(
            _env.Acme.Employee.Email,
            subject: $"Re: [{raised.RecordNumber}] Wifi drops out",
            body: "Still happening.");

        var appended = await ReceiveAsync(key, reply);

        appended.Status.ShouldBe(InboundMessageStatus.AppendedToRecord);
        appended.RecordId.ShouldBe(raised.RecordId);
    }

    [Fact]
    public async Task An_email_from_somebody_this_tenant_does_not_know_is_ignored()
    {
        // An open mailbox that raises a ticket for any address on the internet is a spam target,
        // and the ticket would have no requester anybody could reply to.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);
        var key = (await IssueKeyAsync(admin, "Stranger")).Key;

        var result = await ReceiveAsync(key, NewEmail("nobody@example.invalid"));

        result.Status.ShouldBe(InboundMessageStatus.Ignored);
        result.RecordNumber.ShouldBeNull();
        result.Outcome.ShouldNotBeNull();
        result.Outcome.ShouldContain("nobody@example.invalid");
    }

    [Theory]
    [InlineData("auto-submitted", "auto-replied")]
    [InlineData("list-id", "<announcements.example.com>")]
    [InlineData("precedence", "bulk")]
    public async Task Automated_mail_never_becomes_a_ticket(string header, string value)
    {
        // Two auto-replies answering each other is the classic mail loop, and a desk that raises
        // a ticket for every bounce fills its own queue overnight.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);
        var key = (await IssueKeyAsync(admin, $"Automated {header}")).Key;

        var email = NewEmail(_env.Acme.Employee.Email, subject: "Out of office");
        email.Headers[header] = value;

        var result = await ReceiveAsync(key, email);

        result.Status.ShouldBe(InboundMessageStatus.Ignored);
        result.Outcome.ShouldNotBeNull();
        result.Outcome.ShouldContain("automated");
    }

    [Fact]
    public async Task A_human_reply_marked_auto_submitted_no_is_still_a_ticket()
    {
        // "auto-submitted: no" is the explicit statement that a person sent it — the one value
        // of that header that does not mean automation.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);
        var key = (await IssueKeyAsync(admin, "Explicitly human")).Key;

        var email = NewEmail(_env.Acme.Employee.Email, subject: "A real message");
        email.Headers["auto-submitted"] = "no";

        (await ReceiveAsync(key, email)).Status.ShouldBe(InboundMessageStatus.RecordCreated);
    }

    [Fact]
    public async Task A_message_with_no_message_id_is_refused_rather_than_guessed_at()
    {
        // Without it there is no way to tell a retry from a new message, and the first provider
        // outage would fill the queue with duplicates.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);
        var key = (await IssueKeyAsync(admin, "No id")).Key;

        var email = NewEmail(_env.Acme.Employee.Email);
        email.MessageId = string.Empty;

        var response = await DeliverAsync(key, email);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("email.message_id_required");
    }

    [Fact]
    public async Task Everything_that_arrives_is_recorded_including_what_was_ignored()
    {
        // "I emailed the service desk and nothing happened" is the question this module gets
        // asked, and it cannot be answered from a record of successes.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);
        var key = (await IssueKeyAsync(admin, "History")).Key;

        var ignored = NewEmail("stranger@example.invalid", subject: "Went nowhere");
        await ReceiveAsync(key, ignored);

        var history = await admin.GetFromJsonAsync<PagedResult<InboundMessageDto>>(
            "/api/v1/integration/email/messages?status=Ignored", TestEnvironment.Json);

        history!.Items.ShouldContain(m => m.ExternalMessageId == ignored.MessageId);

        var recorded = history.Items.First(m => m.ExternalMessageId == ignored.MessageId);
        recorded.Outcome.ShouldNotBeNullOrWhiteSpace();
        recorded.Subject.ShouldBe("Went nowhere");
    }

    // -----------------------------------------------------------------
    // Isolation
    // -----------------------------------------------------------------

    [Fact]
    public async Task A_key_belongs_to_one_tenant_and_cannot_act_in_another()
    {
        // The tenant comes from the key, never from the request. A message naming somebody in a
        // neighbouring tenant reads as a stranger here.
        var acmeAdmin = await _env.ClientForAsync(_env.Acme.Administrator);
        var key = (await IssueKeyAsync(acmeAdmin, "Acme mailbox")).Key;

        var result = await ReceiveAsync(key, NewEmail(_env.Northwind.Employee.Email));

        result.Status.ShouldBe(InboundMessageStatus.Ignored);
    }

    [Fact]
    public async Task A_subject_naming_another_tenants_ticket_never_reaches_it()
    {
        // The number in a subject is a candidate, resolved in the caller's own tenant. Note what
        // that does and does not promise: record numbers are unique per tenant, not globally, so
        // a neighbour's INC0000005 is very often also a real INC0000005 here. The guarantee is
        // not that such a subject fails to match — it is that whatever it matches is ours.
        var northwindAdmin = await _env.ClientForAsync(_env.Northwind.Administrator);
        var theirKey = (await IssueKeyAsync(northwindAdmin, "Northwind mailbox")).Key;

        var theirs = await ReceiveAsync(
            theirKey, NewEmail(_env.Northwind.Employee.Email, subject: "A neighbour's problem"));

        var theirComments = await CommentCountAsync(
            await _env.ClientForAsync(_env.Northwind.Manager), theirs.RecordId!.Value);

        var acmeAdmin = await _env.ClientForAsync(_env.Acme.Administrator);
        var ourKey = (await IssueKeyAsync(acmeAdmin, "Acme mailbox two")).Key;

        var result = await ReceiveAsync(
            ourKey,
            NewEmail(_env.Acme.Employee.Email, subject: $"Re: [{theirs.RecordNumber}] Something else"));

        // Whatever became of it, it was not their ticket.
        result.RecordId.ShouldNotBe(theirs.RecordId);

        var neighbour = await _env.ClientForAsync(_env.Northwind.Manager);

        // And nothing was appended to theirs.
        (await CommentCountAsync(neighbour, theirs.RecordId.Value)).ShouldBe(theirComments);

        // The record it did touch is Acme's, and invisible to them.
        (await neighbour.GetAsync($"/api/v1/incidents/{result.RecordId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static async Task<int> CommentCountAsync(HttpClient client, Guid incidentId)
    {
        var comments = await client.GetFromJsonAsync<List<IncidentCommentDto>>(
            $"/api/v1/incidents/{incidentId}/comments", TestEnvironment.Json);

        return comments?.Count ?? 0;
    }

    private static InboundEmailCommand NewEmail(
        string from,
        string subject = "Something is broken",
        string? inReplyTo = null,
        string? body = null) => new()
    {
        MessageId = $"<{Guid.NewGuid():N}@mail.example.com>",
        InReplyTo = inReplyTo,
        From = from,
        FromDisplayName = "Test Sender",
        Subject = subject,
        Body = body ?? "Raised by the integration suite to exercise ingestion end to end."
    };

    /// <summary>
    /// Issues a key against a service account that holds the agent role.
    /// <para>
    /// Created through the real administration API rather than seeded, because the interesting
    /// property is that a key holds exactly its account's permissions — which only means
    /// anything if the account got them the ordinary way.
    /// </para>
    /// </summary>
    private async Task<IssuedIntegrationKeyDto> IssueKeyAsync(HttpClient admin, string name)
    {
        var account = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new
            {
                email = $"svc.{Guid.NewGuid():N}@service.example.in",
                firstName = "Mailbox",
                lastName = "Service",
                isServiceAccount = true
            },
            TestEnvironment.Json);

        account.EnsureSuccessStatusCode();
        var created = (await account.Content.ReadFromJsonAsync<CreatedUserDto>(TestEnvironment.Json))!;

        var roles = (await admin.GetFromJsonAsync<List<RoleDetailDto>>(
            "/api/v1/admin/roles", TestEnvironment.Json))!;

        var agentRole = roles.First(r => r.Code == SystemRoles.ServiceDeskAgent);

        var granted = await admin.PutAsJsonAsync(
            $"/api/v1/admin/users/{created.User.Id}/roles",
            new { roleIds = new[] { agentRole.Id } },
            TestEnvironment.Json);

        granted.EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/integration/keys",
            new { name, serviceAccountUserId = created.User.Id, scope = "InboundEmail" },
            TestEnvironment.Json);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Expected 201 but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<IssuedIntegrationKeyDto>(TestEnvironment.Json))!;
    }

    private async Task<HttpResponseMessage> DeliverAsync(string key, InboundEmailCommand email)
    {
        var client = _env.CreateAnonymousClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/integration/email")
        {
            Content = JsonContent.Create(email, options: TestEnvironment.Json)
        };

        request.Headers.Add("X-NexaOps-Key", key);

        return await client.SendAsync(request);
    }

    private async Task<InboundEmailResultDto> ReceiveAsync(string key, InboundEmailCommand email)
    {
        var response = await DeliverAsync(key, email);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Expected success but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<InboundEmailResultDto>(TestEnvironment.Json))!;
    }
}
