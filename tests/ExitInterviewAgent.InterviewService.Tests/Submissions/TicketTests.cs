using System.Net;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Submissions;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.Submissions;

public sealed class TicketTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<TicketIssued> MintAsync(string sub, TestHost? host = null)
    {
        var h = host ?? _host;
        var (response, body) = await h.Client(h.WebToken(sub)).PostAsync("/api/v1/tickets", null).ReadAsync();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return TestRecords.TicketOf(body);
    }

    private Task<(HttpResponseMessage Response, string Body)> RedeemAsync(string? ticket, byte[] record, TestHost? host = null)
    {
        var client = (host ?? _host).Client();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/submissions/ticketed") { Content = TestRecords.Json(record) };
        if (ticket is not null)
        {
            request.Headers.Add(SubmissionHeaders.Ticket, ticket);
        }
        return client.SendAsync(request).ReadAsync();
    }

    private Task<int> CountTicketsAsync(TestHost? host = null) =>
        (host ?? _host).InScopeAsync(async sp => await sp.GetRequiredService<InterviewDbContext>().SubmissionTickets.CountAsync());

    [Fact]
    public async Task A_minted_ticket_is_stored_as_a_hash_with_the_account_and_a_rounded_expiry_and_nothing_else()
    {
        var sub = TestRecords.NewSub();

        var ticket = await MintAsync(sub);

        Assert.True(SecretTokens.IsWellFormedTicket(ticket.Ticket));
        var row = await _host.InScopeAsync(async sp => await sp.GetRequiredService<InterviewDbContext>().SubmissionTickets.SingleAsync(t => t.Sub == sub));
        Assert.Equal(SecretTokens.Hash(ticket.Ticket), row.TokenHash);
        Assert.NotEqual(ticket.Ticket, row.TokenHash);
        Assert.Equal(ticket.ExpiresAt, row.ExpiresAt);
        // Created 12:00:00, ttl 15 min, rounded up to 5 minutes: 12:15. The row does not say the second it was minted.
        Assert.Equal(_host.Clock.GetUtcNow().AddMinutes(15), ticket.ExpiresAt);
        Assert.Equal(0, ticket.ExpiresAt.Second);
    }

    [Fact]
    public async Task The_ticket_expiry_is_rounded_up_so_the_mint_instant_is_not_recoverable_to_the_second()
    {
        _host.Clock.Set(new DateTimeOffset(2026, 10, 5, 12, 3, 41, TimeSpan.Zero));

        var ticket = await MintAsync(TestRecords.NewSub());

        Assert.Equal(new DateTimeOffset(2026, 10, 5, 12, 20, 0, TimeSpan.Zero), ticket.ExpiresAt);
    }

    [Fact]
    public async Task A_ticket_redeems_once_creates_the_ledger_entry_from_the_account_and_deletes_its_row()
    {
        var sub = TestRecords.NewSub();
        var employer = TestRecords.NewEmployer();
        var ticket = await MintAsync(sub);
        var record = TestRecords.Valid(employer);

        var (response, body) = await RedeemAsync(ticket.Ticket, TestRecords.Bytes(record));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(ReceiptCodes.IsWellFormed(TestRecords.ReceiptCodeOf(body)));
        Assert.Equal(0, await CountTicketsAsync());
        // The ledger entry is the one the account would have created itself: the web path now collides with it.
        var (second, secondBody) = await _host.Client(_host.WebToken(sub)).PostAsync("/api/v1/submissions", TestRecords.Json(TestRecords.Valid(employer))).ReadAsync();
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(SubmissionCodes.AlreadySubmitted, TestRecords.Code(secondBody));
    }

    [Fact]
    public async Task A_ticket_is_single_use()
    {
        var ticket = await MintAsync(TestRecords.NewSub());
        await RedeemAsync(ticket.Ticket, TestRecords.Bytes(TestRecords.Valid()));

        var (response, body) = await RedeemAsync(ticket.Ticket, TestRecords.Bytes(TestRecords.Valid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SubmissionCodes.TicketInvalid, TestRecords.Code(body));
    }

    [Fact]
    public async Task A_ticket_is_not_bound_to_an_employer_any_employer_will_do_once()
    {
        var sub = TestRecords.NewSub();
        var first = await MintAsync(sub);
        var second = await MintAsync(sub);

        Assert.Equal(HttpStatusCode.Created, (await RedeemAsync(first.Ticket, TestRecords.Bytes(TestRecords.Valid()))).Response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await RedeemAsync(second.Ticket, TestRecords.Bytes(TestRecords.Valid()))).Response.StatusCode);
    }

    [Fact]
    public async Task An_expired_ticket_is_refused_exactly_like_an_unknown_one()
    {
        var ticket = await MintAsync(TestRecords.NewSub());
        _host.Clock.Advance(TimeSpan.FromMinutes(21));

        var expired = await RedeemAsync(ticket.Ticket, TestRecords.Bytes(TestRecords.Valid()));
        var unknown = await RedeemAsync(SecretTokens.NewTicket(), TestRecords.Bytes(TestRecords.Valid()));
        var missing = await RedeemAsync(null, TestRecords.Bytes(TestRecords.Valid()));
        var malformed = await RedeemAsync("not-a-ticket", TestRecords.Bytes(TestRecords.Valid()));

        foreach (var (response, body) in new[] { expired, unknown, missing, malformed })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(SubmissionCodes.TicketInvalid, TestRecords.Code(body));
        }
        Assert.Equal(expired.Body, unknown.Body.Replace(TestRecords.Code(unknown.Body)!, TestRecords.Code(expired.Body)!)); // same shape
    }

    [Fact]
    public async Task A_ticket_in_the_query_string_is_not_a_ticket()
    {
        var ticket = await MintAsync(TestRecords.NewSub());

        var response = await _host.Client().PostAsync($"/api/v1/submissions/ticketed?ticket={ticket.Ticket}", TestRecords.Json(TestRecords.Valid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, await CountTicketsAsync());
    }

    [Fact]
    public async Task A_record_that_fails_validation_does_not_consume_the_ticket()
    {
        var ticket = await MintAsync(TestRecords.NewSub());
        var bad = TestRecords.Valid(change: r => r["piiMasked"] = false);

        var (response, _) = await RedeemAsync(ticket.Ticket, TestRecords.Bytes(bad));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, await CountTicketsAsync());
        Assert.Equal(HttpStatusCode.Created, (await RedeemAsync(ticket.Ticket, TestRecords.Bytes(TestRecords.Valid()))).Response.StatusCode);
    }

    [Fact]
    public async Task A_duplicate_for_the_employer_does_not_consume_the_ticket()
    {
        var sub = TestRecords.NewSub();
        var employer = TestRecords.NewEmployer();
        await _host.Client(_host.WebToken(sub)).PostAsync("/api/v1/submissions", TestRecords.Json(TestRecords.Valid(employer)));
        var ticket = await MintAsync(sub);

        var (response, body) = await RedeemAsync(ticket.Ticket, TestRecords.Bytes(TestRecords.Valid(employer)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SubmissionCodes.AlreadySubmitted, TestRecords.Code(body));
        Assert.Equal(1, await CountTicketsAsync());
    }

    [Fact]
    public async Task An_account_cannot_hold_more_than_the_configured_number_of_live_tickets()
    {
        var sub = TestRecords.NewSub();
        for (var i = 0; i < 3; i++)
        {
            await MintAsync(sub);
        }

        var (response, body) = await _host.Client(_host.WebToken(sub)).PostAsync("/api/v1/tickets", null).ReadAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(SubmissionCodes.TicketLimit, TestRecords.Code(body));
        Assert.Equal(3, await CountTicketsAsync());
    }

    [Fact]
    public async Task Minting_is_rate_limited_per_account_not_per_address()
    {
        using var host = new TestHost(settings: new() { ["Submission:Tickets:MintsPerAccountPerHour"] = "2", ["Submission:Tickets:MaxOutstandingPerAccount"] = "50" });
        var busy = TestRecords.NewSub();
        var other = TestRecords.NewSub();

        await MintAsync(busy, host);
        await MintAsync(busy, host);
        var third = await host.Client(host.WebToken(busy)).PostAsync("/api/v1/tickets", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        await MintAsync(other, host); // another account behind the same address is unaffected
    }

    [Fact]
    public async Task Ticketed_submission_is_rate_limited_per_client_with_a_uniform_429()
    {
        using var host = new TestHost(settings: new() { ["Submission:Limits:TicketedSubmitPerIpPerMinute"] = "3" });
        HttpResponseMessage? last = null;

        for (var i = 0; i < 4; i++)
        {
            last = (await RedeemAsync(SecretTokens.NewTicket(), TestRecords.Bytes(TestRecords.Valid()), host)).Response;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.True(last.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task The_global_budget_stops_every_client_together()
    {
        using var host = new TestHost(settings: new() { ["Submission:Limits:TicketedSubmitGlobalPerMinute"] = "2", ["Submission:Limits:TicketedSubmitPerIpPerMinute"] = "100" });
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await RedeemAsync(SecretTokens.NewTicket(), TestRecords.Bytes(TestRecords.Valid()), host)).Response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task The_forwarded_client_header_is_ignored_unless_configuration_trusts_it()
    {
        // Untrusted: rotating a spoofed header must not buy a fresh bucket.
        using var untrusted = new TestHost(settings: new() { ["Submission:Limits:TicketedSubmitPerIpPerMinute"] = "2" });
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/submissions/ticketed") { Content = TestRecords.Json(TestRecords.Valid()) };
            request.Headers.Add("Fly-Client-IP", $"203.0.113.{i}");
            statuses.Add((await untrusted.Client().SendAsync(request)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[2]);

        // Trusted: each real client gets its own bucket.
        using var trusted = new TestHost(settings: new() { ["Submission:Limits:TicketedSubmitPerIpPerMinute"] = "2", ["Submission:ClientIpHeader"] = "Fly-Client-IP" });
        statuses.Clear();
        for (var i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/submissions/ticketed") { Content = TestRecords.Json(TestRecords.Valid()) };
            request.Headers.Add("Fly-Client-IP", $"203.0.113.{i}");
            statuses.Add((await trusted.Client().SendAsync(request)).StatusCode);
        }
        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
    }
}
