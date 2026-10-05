using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using System.Net;
using System.Text;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Submissions;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.InterviewService.Tests.Logging;

/// <summary>
/// Everything the process emits while it handles submissions, tickets and receipts is captured: every log line (message,
/// structured arguments, exception text), every activity tag, event, status and baggage, every .NET event-source
/// payload, every metric tag. The canaries below are unique strings that stand for the things that must never leave the
/// process through telemetry (T-15, brief section 6): quote text, employer, account subject, a ticket, a receipt code, a
/// bearer header, an exception message. The test has teeth: the capture itself is proven to see each channel, and a body-logging
/// regression is proven to be caught.
/// </summary>
public sealed class ContentCanaryTests
{
    private const string Quote = "zzquotecanary7731";
    private const string Employer = "emp-zzemployercanary";
    private const string SubCanary = "acct-zzsubcanary5512";
    private const string Header = "zzheadercanary9084";
    private const string BadCode = "zzreceiptcodecanary-0123456789abcdefghijk";   // malformed receipt code (wrong checksum)
    private const string BadTicket = "zzticketcanary0123456789abcdefghijklmnopqrs"; // 43 chars, not a real ticket
    private const string Exception = "zzexceptioncanary4419";

    private static HttpRequestMessage Delete(string code)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/receipts");
        request.Headers.Add(SubmissionHeaders.ReceiptCode, code);
        return request;
    }

    private static async Task<string> RunScenarioAsync(TestHost host)
    {
        var web = host.Client(host.WebToken(SubCanary));
        web.DefaultRequestHeaders.Add("X-Canary", Header);
        // The User-Agent is deliberately not a canary: the standard HTTP server span records it (user_agent.original), a client-chosen,
        // non-content value. Method, route, status, User-Agent and timing are the T-09 residual, documented in the threat model.
        var anonymous = host.Client();
        anonymous.DefaultRequestHeaders.Add("X-Canary", Header);
        var secrets = new StringBuilder();

        // 1. A normal submission whose quote and employer are canaries: the receipt code comes back once.
        var ok = TestRecords.Valid(Employer, r => r.SetQuotes("culture", $"The team was {Quote} indeed."));
        var (accepted, acceptedBody) = await web.PostAsync("/api/v1/submissions", TestRecords.Json(ok)).ReadAsync();
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var receipt = TestRecords.ReceiptCodeOf(acceptedBody);
        secrets.Append(receipt).Append('\n');

        // 2. The duplicate, a schema violation and a PII rejection, each carrying the canary in its content.
        await web.PostAsync("/api/v1/submissions", TestRecords.Json(TestRecords.Valid(Employer, r => r.SetQuotes("culture", Quote))));
        await web.PostAsync("/api/v1/submissions", TestRecords.Json(TestRecords.Valid(change: r => { r["x" + Quote] = Quote; r["employerRef"] = Employer; })));
        await web.PostAsync("/api/v1/submissions", TestRecords.Json(TestRecords.Valid(change: r => r.SetQuotes("culture", $"{Quote} write to {Quote}@example.org"))));
        await web.PostAsync("/api/v1/submissions", new StringContent($"{{ \"broken\": \"{Quote}\" ", Encoding.UTF8, "application/json"));
        await web.PostAsync("/api/v1/submissions", TestRecords.Json(new byte[200 * 1024]));

        // 3. Tickets: mint, redeem, then use a bad and a reused ticket.
        var (_, mintBody) = await web.PostAsync("/api/v1/tickets", null).ReadAsync();
        var ticket = TestRecords.TicketOf(mintBody).Ticket;
        secrets.Append(ticket).Append('\n');
        foreach (var presented in new[] { ticket, ticket, BadTicket })
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/submissions/ticketed") { Content = TestRecords.Json(TestRecords.Valid(Employer + "x", r => r.SetQuotes("growth", Quote))) };
            request.Headers.Add(SubmissionHeaders.Ticket, presented);
            await anonymous.SendAsync(request);
        }

        // 4. The MCP path.
        await McpWire.CallToolAsync(host.Client(host.McpToken(SubCanary)), "submit_interview_record", TestRecords.Valid(Employer + "y"));

        // 5. Receipt deletion: known, again, unknown, malformed.
        await anonymous.SendAsync(Delete(receipt));
        await anonymous.SendAsync(Delete(receipt));
        await anonymous.SendAsync(Delete(ReceiptCodes.NewCode()));
        await anonymous.SendAsync(Delete(BadCode));

        // 6. Retention sweep over the data above.
        host.Clock.Advance(TimeSpan.FromDays(800));
        await host.InScopeAsync(sp => sp.GetRequiredService<RetentionPurger>().PurgeAsync(default));

        return secrets.ToString();
    }

    private static readonly string[] Canaries = [Quote, Employer, SubCanary, Header, BadCode, BadTicket, Exception, "emp-zzemployercanaryx", "emp-zzemployercanaryy"];

    private static TestHost NewHost(Capture capture, string? postgres = null, Action<IServiceCollection>? services = null) =>
        new(new() { ["Submission:Receipts:ResponseFloorMilliseconds"] = "0", ["Submission:Limits:ReceiptDeletePerIpPerMinute"] = "1000", ["Submission:Limits:TicketedSubmitPerIpPerMinute"] = "1000" },
            postgres, services, capture.Logs);

    private static void AssertClean(string everything, IEnumerable<string> canaries)
    {
        foreach (var canary in canaries)
        {
            var at = everything.IndexOf(canary, StringComparison.OrdinalIgnoreCase);
            Assert.False(at >= 0, at < 0 ? "" : $"canary '{canary[..Math.Min(20, canary.Length)]}...' leaked into telemetry: ...{everything[Math.Max(0, at - 200)..Math.Min(everything.Length, at + 60)]}");
        }
    }

    [Fact]
    public async Task No_canary_reaches_a_log_line_a_trace_attribute_an_event_an_exception_message_or_a_metric_label()
    {
        using var capture = new Capture();
        using var host = NewHost(capture, services: s =>
        {
            // A scanner and a verifier that fail with the canary in their exception message: the failure paths run too.
            s.AddSingleton<IEmploymentVerifier>(new ThrowingVerifier());
        });

        var secrets = await RunScenarioAsync(host);

        var everything = capture.Everything;
        Assert.True(everything.Length > 2000, "the capture saw almost nothing; the test would pass for the wrong reason");
        AssertClean(everything, Canaries.Concat(secrets.Split('\n', StringSplitOptions.RemoveEmptyEntries)));
    }

    [PostgresFact]
    public async Task No_canary_reaches_telemetry_on_PostgreSQL_either_where_the_database_logs_its_commands()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var capture = new Capture();
        using var host = NewHost(capture, pg.ConnectionString);
        await host.WaitReadyAsync();

        var secrets = await RunScenarioAsync(host);

        var everything = capture.Everything;
        Assert.Contains("Executed DbCommand", everything); // the SQL really was logged, so the parameters were in scope of the check
        AssertClean(everything, Canaries.Concat(secrets.Split('\n', StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public void The_capture_sees_every_channel_so_a_clean_result_means_something()
    {
        using var capture = new Capture();
        using var host = new TestHost(logs: capture.Logs);
        const string probe = "zzprobe8841";

        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("canary").LogInformation("message {Arg}", probe + "-arg");
        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("canary").LogError(new InvalidOperationException(probe + "-exception"), "failed");
        using (var source = new ActivitySource("canary-test"))
        {
            using var activity = source.StartActivity("op");
            activity?.SetTag("attr", probe + "-tag");
            activity?.AddEvent(new ActivityEvent("evt", tags: new ActivityTagsCollection { ["k"] = probe + "-event" }));
            activity?.SetStatus(ActivityStatusCode.Error, probe + "-status");
        }
        using (var meter = new Meter("canary-meter"))
        {
            meter.CreateCounter<long>("c").Add(1, new KeyValuePair<string, object?>("label", probe + "-label"));
        }
        using (var events = new CanaryEventSource())
        {
            events.Emit(probe + "-eventsource");
        }

        var everything = capture.Everything;
        foreach (var channel in new[] { "-arg", "-exception", "-tag", "-event", "-status", "-label", "-eventsource" })
        {
            Assert.Contains(probe + channel, everything);
        }
    }

    [Fact]
    public async Task A_body_logging_regression_is_caught_by_the_same_scenario()
    {
        using var capture = new Capture();
        using var host = NewHost(capture, services: s => s.AddTransient<IStartupFilter, BodyLoggingFilter>());

        await RunScenarioAsync(host);

        Assert.Contains(Quote, capture.Everything); // the leak is visible: the canary assertions above would fail on this host
    }

    [Fact]
    public async Task A_regression_that_logs_the_receipt_code_or_the_account_is_caught_too()
    {
        using var capture = new Capture();
        using var host = NewHost(capture, services: s => s.AddTransient<IStartupFilter, HeaderLoggingFilter>());

        var secrets = await RunScenarioAsync(host);

        var everything = capture.Everything;
        Assert.Contains(secrets.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0], everything);
    }

    private sealed class ThrowingVerifier : IEmploymentVerifier
    {
        public Task<EmploymentCheck> VerifyAsync(string subject, string employerRef, CancellationToken ct) =>
            throw new HttpRequestException($"registry error for {employerRef} / {subject} / {Exception}");
    }

    private sealed class BodyLoggingFilter(ILoggerFactory logs) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
                logs.CreateLogger("regression").LogInformation("body: {Body}", await reader.ReadToEndAsync());
                context.Request.Body.Position = 0;
                await nextMiddleware();
            });
            next(app);
        };
    }

    private sealed class HeaderLoggingFilter(ILoggerFactory logs) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                logs.CreateLogger("regression").LogInformation("headers: {Headers}", string.Join(';', context.Request.Headers.Select(h => $"{h.Key}={h.Value}")));
                await nextMiddleware();
            });
            next(app);
        };
    }

    [EventSource(Name = "ExitInterviewAgent-CanaryTest")]
    private sealed class CanaryEventSource : EventSource
    {
        [Event(1)]
        public void Emit(string text) => WriteEvent(1, text);
    }
}
