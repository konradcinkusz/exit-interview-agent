using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Cli.Tests.Support;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.Providers;

namespace ExitInterviewAgent.Cli.Tests;

public sealed partial class SubmitCliTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eia-submit-").FullName;
    private readonly FakeSubmissionBackend _backend = new();
    private const string Ticket = CliRun.CanaryTicket;

    public void Dispose()
    {
        _backend.Dispose();
        Directory.Delete(_dir, true);
    }

    private string WriteRecord(Action<System.Text.Json.Nodes.JsonObject>? change = null)
    {
        var path = Path.Combine(_dir, "record.json");
        File.WriteAllText(path, TestRecord.Pretty(TestRecord.Valid(change)));
        return path;
    }

    private string Server => _backend.BaseUrl.GetLeftPart(UriPartial.Authority);

    private Task<CliResult> Submit(string stdin, string[]? extra = null, params (string, string)[] env) =>
        CliRun.RunAsync(["submit", "--record", WriteRecord(), "--server", Server, .. extra ?? []], stdin, CliRun.Env(env));

    [GeneratedRegex("[A-Za-z0-9_-]{46}")]
    private static partial Regex Receipt();

    // ---- the happy path and what is on the wire -------------------------------------------------------------------

    [Fact]
    public async Task A_submission_sends_exactly_the_shown_record_with_one_secret_header_and_shows_the_receipt_once()
    {
        _backend.ValidTickets.Add(Ticket);
        var path = WriteRecord();

        var r = await CliRun.RunAsync(["submit", "--record", path, "--server", Server], $"submit\n{Ticket}\n", CliRun.Env());

        Assert.Equal(0, r.Code);
        var request = Assert.Single(_backend.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(FakeSubmissionBackend.TicketPath, request.PathAndQuery); // no query string: nothing in the URL
        Assert.Equal(File.ReadAllBytes(path), request.Body);                 // the very bytes of the file
        Assert.Contains(TestRecord.CanaryQuote, r.Out);                          // ...which were shown first
        Assert.Equal(Ticket, request.Header("X-Submission-Ticket"));
        Assert.Equal("exit-interview", request.Header("User-Agent"));
        Assert.Equal("application/json", request.Header("Content-Type"));
        var custom = request.Headers.Keys.Where(k => k.StartsWith("X-", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(["X-Submission-Ticket"], custom);
        Assert.DoesNotContain(request.Headers.Keys, k => k.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || k.Equals("Cookie", StringComparison.OrdinalIgnoreCase));

        var receipt = Assert.Single(Receipt().Matches(r.Out.Split("== Your receipt code (shown once) ==")[1].Split("This is the only way")[0])).Value;
        Assert.Single(Regex.Matches(r.Out, Regex.Escape(receipt)));            // exactly once
        Assert.Contains("only way to delete this record", r.Out);
        Assert.Contains("cannot find or re-issue it for you", r.Out);
        Assert.Contains($"exit-interview delete-receipt --server {Server}", r.Out);
        Assert.DoesNotContain(receipt, r.Err);
        Assert.DoesNotContain(Ticket, r.All);
        Assert.Contains("== What will leave this computer ==", r.Out);
        Assert.Contains("no transcript", r.Out);
    }

    [Fact]
    public async Task The_ticket_can_come_from_the_environment_and_the_confirmation_can_be_skipped_with_yes()
    {
        _backend.ValidTickets.Add(Ticket);

        var r = await Submit(string.Empty, ["--yes"], ("EXIT_INTERVIEW_TICKET", Ticket));

        Assert.Equal(0, r.Code);
        Assert.Contains("--yes given", r.Out);
        Assert.Single(_backend.Requests);
        Assert.DoesNotContain(Ticket, r.All);
    }

    [Fact]
    public async Task The_record_can_come_from_standard_input_but_then_yes_and_an_environment_ticket_are_required()
    {
        _backend.ValidTickets.Add(Ticket);
        var json = TestRecord.Pretty(TestRecord.Valid());

        var missing = await CliRun.RunAsync(["submit", "--record", "-", "--server", Server], json, CliRun.Env());
        var noTicket = await CliRun.RunAsync(["submit", "--record", "-", "--server", Server, "--yes"], json, CliRun.Env());
        var ok = await CliRun.RunAsync(["submit", "--record", "-", "--server", Server, "--yes"], json, CliRun.Env(("EXIT_INTERVIEW_TICKET", Ticket)));

        Assert.Equal(2, missing.Code);
        Assert.Equal(2, noTicket.Code);
        Assert.Equal(0, ok.Code);
        Assert.Equal(Encoding.UTF8.GetBytes(json), Assert.Single(_backend.Requests).Body);
    }

    // ---- there is no flag for a secret ------------------------------------------------------------------------------

    [Theory]
    [InlineData("--ticket")]
    [InlineData("--token")]
    [InlineData("--receipt-code")]
    [InlineData("--receipt")]
    public async Task A_secret_has_no_command_line_flag_and_the_value_is_not_echoed(string flag)
    {
        var r = await Submit(string.Empty, [flag, Ticket], ("EXIT_INTERVIEW_TICKET", Ticket));
        var d = await CliRun.RunAsync(["delete-receipt", "--server", Server, flag, CliRun.CanaryReceipt], string.Empty, CliRun.Env());

        Assert.Equal(2, r.Code);
        Assert.Equal(2, d.Code);
        Assert.Contains("Unknown option", r.Err);
        Assert.DoesNotContain(Ticket, r.All);
        Assert.DoesNotContain(CliRun.CanaryReceipt, d.All);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void No_command_declares_a_flag_that_would_carry_a_secret()
    {
        var secretWords = new[] { "ticket", "token", "secret", "password", "receipt", "key", "apikey", "credential", "code" };
        foreach (var (command, flags) in CliFlags.ByCommand)
            foreach (var flag in flags)
            {
                // The one allowed shape: a flag that NAMES an environment variable (--api-key-env), never one that holds a value.
                if (flag.EndsWith("-env", StringComparison.Ordinal)) continue;
                var offending = secretWords.FirstOrDefault(w => flag != "--save-receipt" && flag.Split('-', StringSplitOptions.RemoveEmptyEntries).Contains(w, StringComparer.OrdinalIgnoreCase));
                Assert.True(offending is null, $"'{command}' declares {flag}: secrets must come from the environment, a hidden prompt or standard input, never argv.");
            }
        Assert.DoesNotContain("--ticket", CliApp.Usage);
        Assert.DoesNotContain("--receipt-code", CliApp.Usage);
    }

    // ---- confirmation ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("no\n")]
    [InlineData("yes\n")]
    [InlineData("y\n")]
    [InlineData("send it\n")]
    public async Task Anything_but_the_typed_word_keeps_the_record_on_this_computer(string stdin)
    {
        _backend.ValidTickets.Add(Ticket);

        var r = await Submit(stdin + Ticket + "\n");

        Assert.Equal(3, r.Code);
        Assert.Contains("Not confirmed. Nothing was sent.", r.Out);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task The_ticket_is_asked_for_only_after_the_confirmation()
    {
        var r = await Submit("no\n", env: ("EXIT_INTERVIEW_TICKET", "short")); // would be an error if it were read before the confirmation

        Assert.Equal(3, r.Code);
    }

    [Fact]
    public async Task A_cancelled_confirmation_sends_nothing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var r = await CliRun.RunAsync(["submit", "--record", WriteRecord(), "--server", Server], string.Empty, CliRun.Env(), ct: cts.Token);

        Assert.Equal(3, r.Code);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task Without_a_terminal_the_ticket_is_read_as_one_plain_line_and_a_missing_one_sends_nothing()
    {
        var none = await Submit("submit\n");
        var bad = await Submit($"submit\nnot a ticket!\n");

        Assert.Equal(3, none.Code);
        Assert.Contains("No ticket given", none.Out);
        Assert.Equal(2, bad.Code);
        Assert.Contains("does not look like a ticket", bad.Err);
        Assert.DoesNotContain("not a ticket!", bad.All);
        Assert.Empty(_backend.Requests);
    }

    private sealed class FakePrompt(string? answer, bool interactive = true) : ISecretPrompt
    {
        public int Asked { get; private set; }
        public bool IsInteractive => interactive;
        public string? Read(string prompt, TextWriter output, CancellationToken ct) { Asked++; return answer; }
    }

    [Fact]
    public async Task With_a_terminal_the_hidden_prompt_is_used_and_stdin_is_left_alone()
    {
        _backend.ValidTickets.Add(Ticket);
        var prompt = new FakePrompt(Ticket);

        var r = await CliRun.RunAsync(["submit", "--record", WriteRecord(), "--server", Server], "submit\nSTDIN-LINE-MUST-NOT-BE-READ\n", CliRun.Env(), prompt: prompt);

        Assert.Equal(0, r.Code);
        Assert.Equal(1, prompt.Asked);
        Assert.DoesNotContain(Ticket, r.All);
    }

    [Fact]
    public async Task The_environment_wins_over_the_prompt_and_a_non_interactive_prompt_falls_back_to_stdin()
    {
        _backend.ValidTickets.Add(Ticket);
        var viaEnv = new FakePrompt("never");
        var env = await CliRun.RunAsync(["submit", "--record", WriteRecord(), "--server", Server, "--yes"], string.Empty, CliRun.Env(("EXIT_INTERVIEW_TICKET", Ticket)), prompt: viaEnv);
        _backend.ValidTickets.Add(Ticket);
        var piped = new FakePrompt("never", interactive: false);
        var stdin = await CliRun.RunAsync(["submit", "--record", WriteRecord(), "--server", Server, "--yes"], Ticket + "\n", CliRun.Env(), prompt: piped);

        Assert.Equal(0, env.Code);
        Assert.Equal(0, viaEnv.Asked);
        Assert.Equal(0, stdin.Code);
        Assert.Equal(0, piped.Asked);
    }

    // ---- the local check, fail closed --------------------------------------------------------------------------

    [Fact]
    public async Task A_record_with_personal_data_is_stopped_here_naming_the_field_and_kind_and_never_the_text()
    {
        const string email = "jan.kowalski@example.com";
        var path = WriteRecord(r => r["topics"]!["management"]!["quotes"] = new System.Text.Json.Nodes.JsonArray($"Write to {email} if you disagree."));

        var r = await CliRun.RunAsync(["submit", "--record", path, "--server", Server, "--yes"], string.Empty, CliRun.Env(("EXIT_INTERVIEW_TICKET", Ticket)));

        Assert.Equal(4, r.Code);
        Assert.Contains("an email address at /topics/management/quotes/0", r.Err);
        Assert.DoesNotContain(email, r.All);
        Assert.Empty(_backend.Requests);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    public async Task A_file_that_is_not_a_valid_record_is_stopped_here(string content)
    {
        var path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, content);

        var r = await CliRun.RunAsync(["submit", "--record", path, "--server", Server, "--yes"], string.Empty, CliRun.Env(("EXIT_INTERVIEW_TICKET", Ticket)));

        Assert.Equal(4, r.Code);
        Assert.Contains("cannot be submitted", r.Err);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task A_record_without_ai_disclosure_or_without_content_is_stopped_here()
    {
        var undisclosed = WriteRecord(r => r["interview"]!["aiDisclosed"] = false);
        var a = await CliRun.RunAsync(["submit", "--record", undisclosed, "--server", Server, "--yes"], string.Empty, CliRun.Env(("EXIT_INTERVIEW_TICKET", Ticket)));

        Assert.Equal(4, a.Code);
        Assert.Contains("AI", a.Err);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task A_missing_file_and_an_oversized_file_are_usage_errors()
    {
        var big = Path.Combine(_dir, "big.json");
        File.WriteAllBytes(big, new byte[200 * 1024]);

        var missing = await CliRun.RunAsync(["submit", "--record", Path.Combine(_dir, "nope.json"), "--server", Server], string.Empty, CliRun.Env());
        var large = await CliRun.RunAsync(["submit", "--record", big, "--server", Server], string.Empty, CliRun.Env());
        var none = await CliRun.RunAsync(["submit", "--server", Server], string.Empty, CliRun.Env());

        Assert.Equal([2, 2, 2], [missing.Code, large.Code, none.Code]);
    }

    // ---- every server answer has a plain message --------------------------------------------------------------------

    public static TheoryData<int, string, string> Answers => new()
    {
        { 401, "TICKET_INVALID", "Mint a new ticket on the web panel's /cli page" },
        { 403, "EMPLOYMENT_NOT_VERIFIED", "could not confirm that your account is linked" },
        { 409, "ALREADY_SUBMITTED", "already submitted a record for this employer" },
        { 409, "INTERVIEW_ID_TAKEN", "interview id is already stored" },
        { 413, "PAYLOAD_TOO_LARGE", "too large" },
        { 400, "NOT_JSON", "could not read the file as a record" },
        { 400, "DUPLICATE_KEY", "could not read the file as a record" },
        { 400, "NESTING_TOO_DEEP", "could not read the file as a record" },
        { 422, "AI_NOT_DISCLOSED", "not told the interviewer is an AI" },
        { 422, "PII_CHECK_FAILED", "could not finish" },
        { 422, "MISSING_FIELD", "format" },
        { 422, "UNKNOWN_FIELD", "format" },
        { 422, "LENGTH_LIMIT", "format" },
        { 422, "PII_NOT_MASKED", "format" },
        { 429, "TICKET_LIMIT", "too many requests" },
        { 500, "BOOM", "internal problem" },
        { 404, "NOPE", "does not have this endpoint" },
        { 418, "TEAPOT", "refused the request" },
    };

    [Theory]
    [MemberData(nameof(Answers))]
    public async Task Every_server_answer_becomes_a_short_actionable_message_that_leaks_nothing(int status, string code, string expected)
    {
        _backend.Script = _ => FakeSubmissionBackend.Problem(status, code, errors: code is "MISSING_FIELD" or "UNKNOWN_FIELD" or "LENGTH_LIMIT" or "PII_NOT_MASKED" ? [(code, "/topics/culture/quotes/0")] : null);

        var r = await Submit(string.Empty, ["--yes"], ("EXIT_INTERVIEW_TICKET", Ticket));

        Assert.Equal(5, r.Code);
        Assert.Contains(expected, r.Err, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(code, r.Err);
        Assert.DoesNotContain(Ticket, r.All);
        Assert.DoesNotContain(TestRecord.CanaryQuote, r.Err);
    }

    [Fact]
    public async Task A_server_side_personal_data_finding_names_kinds_only_and_says_what_to_do()
    {
        _backend.Script = _ => FakeSubmissionBackend.Problem(422, "PII_DETECTED", kinds: ["Email", "PersonName"]);

        var r = await Submit(string.Empty, ["--yes"], ("EXIT_INTERVIEW_TICKET", Ticket));

        Assert.Equal(5, r.Code);
        Assert.Contains("an email address, a person's name", r.Err);
        Assert.Contains("Nothing was stored", r.Err);
        Assert.Contains("[PERSON]", r.Err);
        Assert.DoesNotContain(TestRecord.CanaryQuote, r.All.Replace(TestRecord.Pretty(TestRecord.Valid()), string.Empty).Split("== The record that will be sent ==")[0] + r.Err);
    }

    [Fact]
    public async Task A_rate_limit_shows_how_long_to_wait_from_Retry_After_and_from_the_service_body()
    {
        _backend.Script = _ => FakeSubmissionBackend.RateLimited(42);
        var r = await Submit(string.Empty, ["--yes"], ("EXIT_INTERVIEW_TICKET", Ticket));
        _backend.Script = _ => new FakeReply(429, "{}");
        var bare = await Submit(string.Empty, ["--yes"], ("EXIT_INTERVIEW_TICKET", Ticket));

        Assert.Equal(5, r.Code);
        Assert.Contains("42 seconds", r.Err);
        Assert.Contains("RATE_LIMITED", r.Err);
        Assert.Contains("Wait a minute", bare.Err);
    }

    [Fact]
    public async Task A_hostile_answer_cannot_put_text_or_control_characters_on_the_terminal()
    {
        _backend.Script = _ => new FakeReply(422, """{"code":"\u001b[2JOWNED","title":"\u001b[31mred","errors":[{"code":"LENGTH_LIMIT","path":"/x\u001b[2J"},{"code":"WRONG_TYPE","path":"/ok/path"}],"kinds":["Email\u001b[0m","Phone"]}""", "application/problem+json");

        var r = await Submit(string.Empty, ["--yes"], ("EXIT_INTERVIEW_TICKET", Ticket));

        Assert.Equal(5, r.Code);
        Assert.DoesNotContain('\u001b', r.Err);
        Assert.DoesNotContain("OWNED", r.Err);
        Assert.Contains("UNKNOWN", r.Err);
    }

    [Fact]
    public async Task A_oversized_or_malformed_success_is_not_trusted()
    {
        _backend.Script = _ => new FakeReply(201, "{\"receiptCode\":\"has spaces and is not a code\"}");
        var malformed = await Submit(string.Empty, ["--yes"], ("EXIT_INTERVIEW_TICKET", Ticket));
        _backend.Script = _ => new FakeReply(200, PadBody: 200 * 1024);
        var huge = await Submit(string.Empty, ["--yes"], ("EXIT_INTERVIEW_TICKET", Ticket));

        Assert.Equal(6, malformed.Code);
        Assert.Contains("not in the form this protocol uses", malformed.Err);
        Assert.Equal(6, huge.Code);
        Assert.Contains("far larger", huge.Err);
    }

    // ---- redirects ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task A_redirect_is_never_followed_and_the_other_host_never_receives_the_secret(int status)
    {
        using var elsewhere = new FakeSubmissionBackend();
        _backend.Script = _ => new FakeReply(status, Headers: new Dictionary<string, string> { ["Location"] = elsewhere.BaseUrl + "api/v1/submissions/ticketed" });

        var r = await Submit(string.Empty, ["--yes"], ("EXIT_INTERVIEW_TICKET", Ticket));

        Assert.Equal(6, r.Code);
        Assert.Contains("never follows redirects", r.Err);
        Assert.Single(_backend.Requests);
        Assert.Empty(elsewhere.Requests);                      // the secret header went nowhere else
        Assert.DoesNotContain(elsewhere.BaseUrl.Port.ToString(), r.All); // and the Location is not echoed
    }

    // ---- network failures and retries --------------------------------------------------------------------------------

    private sealed class ScriptedHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref Calls);
            await Task.Yield();
            return respond(n);
        }
    }

    private static HttpRequestException Boom(HttpRequestError error) => new(error, "CANARY-EXCEPTION-TEXT https://user:CANARYPW@host/?q=CANARY");

    [Theory]
    [InlineData(HttpRequestError.ConnectionError, 2, "Could not connect")]
    [InlineData(HttpRequestError.NameResolutionError, 2, "did not resolve")]
    [InlineData(HttpRequestError.SecureConnectionError, 1, "certificate or protocol")]
    [InlineData(HttpRequestError.Unknown, 1, "connection broke")]
    [InlineData(HttpRequestError.ExtendedConnectNotSupported, 1, "connection broke")]
    public async Task Only_a_connection_that_could_not_be_opened_is_tried_again_and_only_once(HttpRequestError error, int calls, string message)
    {
        var handler = new ScriptedHandler(_ => throw Boom(error));
        using var client = new SubmissionClient(ServerUrl.Parse(Server), handler, retryDelay: TimeSpan.Zero);
        using var ticket = Secret(Ticket);

        var result = await client.SubmitAsync("{}"u8.ToArray(), ticket, default);

        var failed = Assert.IsType<CallResult.Failed>(result);
        Assert.Equal(calls, handler.Calls);
        var text = string.Join(' ', SubmitMessages.Explain(failed, ServerUrl.Parse(Server), submission: true));
        Assert.Contains(message, text);
        Assert.DoesNotContain("CANARY", text);
    }

    [Fact]
    public async Task A_response_that_arrives_is_never_retried_whatever_it_says()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = new SubmissionClient(ServerUrl.Parse(Server), handler, retryDelay: TimeSpan.Zero);
        using var ticket = Secret(Ticket);

        var result = await client.SubmitAsync("{}"u8.ToArray(), ticket, default);

        Assert.IsType<CallResult.Rejected>(result);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_timeout_says_the_outcome_is_unknown_and_is_not_retried()
    {
        var handler = new SlowHandler();
        using var client = new SubmissionClient(ServerUrl.Parse(Server), handler, timeout: TimeSpan.FromMilliseconds(150));
        using var ticket = Secret(Ticket);

        var result = await client.SubmitAsync("{}"u8.ToArray(), ticket, default);

        Assert.Equal(Failure.TimedOut, Assert.IsType<CallResult.Failed>(result).Reason);
        Assert.Equal(1, handler.Calls);
        Assert.Contains("may or may not have received", string.Join(' ', SubmitMessages.Explain((CallResult.Failed)result, ServerUrl.Parse(Server), true)));
    }

    [Fact]
    public async Task Cancelling_during_the_request_stops_it_and_says_so()
    {
        var handler = new SlowHandler();
        using var client = new SubmissionClient(ServerUrl.Parse(Server), handler);
        using var ticket = Secret(Ticket);
        using var cts = new CancellationTokenSource(100);

        var result = await client.SubmitAsync("{}"u8.ToArray(), ticket, cts.Token);

        Assert.Equal(Failure.Cancelled, Assert.IsType<CallResult.Failed>(result).Reason);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException();
        }
    }

    [Fact]
    public async Task A_closed_port_is_reported_without_the_address_details_beyond_scheme_host_and_port()
    {
        var port = FakeSubmissionBackend.FreePort();

        var r = await CliRun.RunAsync(["submit", "--record", WriteRecord(), "--server", $"http://127.0.0.1:{port}", "--yes"], string.Empty, CliRun.Env(("EXIT_INTERVIEW_TICKET", Ticket)));

        Assert.Equal(6, r.Code);
        Assert.Contains("Could not connect to http://127.0.0.1:" + port, r.Err);
        Assert.Contains("Nothing was sent", r.Err);
        Assert.DoesNotContain(Ticket, r.All);
    }

    [Fact]
    public void The_real_handler_is_built_without_redirects_cookies_or_decompression_and_with_timeouts()
    {
        using var handler = SubmissionClient.CreateHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Equal(TimeSpan.FromSeconds(10), handler.ConnectTimeout);
        // TLS defaults untouched: no certificate callback, no pinned protocol list, no client certificates.
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.Equal(System.Security.Authentication.SslProtocols.None, handler.SslOptions.EnabledSslProtocols);
        Assert.Null(handler.SslOptions.ClientCertificates);
    }

    private static RedactedSecret Secret(string text)
    {
        Assert.True(RedactedSecret.TryCreate(text, out var s));
        return s!;
    }

    // ---- the server address ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("http://example.com", "Plain http://")]
    [InlineData("ftp://example.com", "must start with https://")]
    [InlineData("example.com", "not a valid absolute URL")]
    [InlineData("https://example.com/prefix", "bare origin")]
    [InlineData("https://example.com/?a=b", "query string")]
    [InlineData("https://example.com/#frag", "query string or fragment")]
    [InlineData("https://user:CANARYPASSWORD@example.com", "user name or password")]
    [InlineData("https://example.com/?token=CANARYQUERY", "query string")]
    public async Task An_unacceptable_server_address_is_refused_without_repeating_it(string url, string reason)
    {
        var r = await CliRun.RunAsync(["submit", "--record", WriteRecord(), "--server", url, "--yes"], string.Empty, CliRun.Env(("EXIT_INTERVIEW_TICKET", Ticket)));

        Assert.Equal(2, r.Code);
        Assert.Contains(reason, r.Err);
        Assert.DoesNotContain("CANARY", r.All);
        Assert.DoesNotContain("example.com", r.All);
        Assert.DoesNotContain(Ticket, r.All);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://example.com:8443")]
    [InlineData("http://localhost:5000")]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://[::1]:5000")]
    public void Acceptable_server_addresses(string url) => Assert.NotNull(ServerUrl.Parse(url));

    [Fact]
    public async Task There_is_no_default_server_and_the_flag_wins_over_the_environment()
    {
        var none = await CliRun.RunAsync(["submit", "--record", WriteRecord(), "--yes"], string.Empty, CliRun.Env(("EXIT_INTERVIEW_TICKET", Ticket)));
        _backend.ValidTickets.Add(Ticket);
        var both = await CliRun.RunAsync(["submit", "--record", WriteRecord(), "--yes", "--server", Server], string.Empty, CliRun.Env(("EXIT_INTERVIEW_TICKET", Ticket), ("EXIT_INTERVIEW_SERVER_URL", "https://wrong.invalid")));
        _backend.ValidTickets.Add(Ticket);
        var env = await CliRun.RunAsync(["submit", "--record", WriteRecord(), "--yes"], string.Empty, CliRun.Env(("EXIT_INTERVIEW_TICKET", Ticket), ("EXIT_INTERVIEW_SERVER_URL", Server)));

        Assert.Equal(2, none.Code);
        Assert.Contains("EXIT_INTERVIEW_SERVER_URL", none.Err);
        Assert.Contains("no default", none.Err.Replace("deliberately ", string.Empty));
        Assert.Equal(0, both.Code);
        Assert.Equal(0, env.Code);
    }

    // ---- the receipt file ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_receipt_file_is_created_private_never_overwrites_and_is_checked_before_anything_is_sent()
    {
        _backend.ValidTickets.Add(Ticket);
        var file = Path.Combine(_dir, "receipt.txt");

        var first = await Submit(string.Empty, ["--yes", "--save-receipt", file], ("EXIT_INTERVIEW_TICKET", Ticket));

        Assert.Equal(0, first.Code);
        var saved = File.ReadAllText(file);
        Assert.Matches("^[A-Za-z0-9_-]{46}\n$", saved);
        Assert.Contains(saved.Trim(), first.Out);
        Assert.Contains(Path.GetFullPath(file), first.Out);
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));

        _backend.ValidTickets.Add(Ticket);
        var requestsBefore = _backend.Requests.Count;
        var second = await Submit(string.Empty, ["--yes", "--save-receipt", file], ("EXIT_INTERVIEW_TICKET", Ticket));
        Assert.Equal(2, second.Code);
        Assert.Contains("will not be overwritten", second.Err);
        Assert.Equal(saved, File.ReadAllText(file));
        Assert.Equal(requestsBefore, _backend.Requests.Count);

        var noDir = await Submit(string.Empty, ["--yes", "--save-receipt", Path.Combine(_dir, "nope", "r.txt")], ("EXIT_INTERVIEW_TICKET", Ticket));
        Assert.Equal(2, noDir.Code);
        Assert.Equal(requestsBefore, _backend.Requests.Count);
    }

    [Fact]
    public void Writing_a_receipt_file_refuses_an_existing_file_even_if_it_appeared_after_the_early_check()
    {
        var file = Path.Combine(_dir, "raced.txt");
        File.WriteAllText(file, "somebody else's content");

        Assert.ThrowsAny<IOException>(() => ReceiptFile.Write(file, CliRun.CanaryReceipt));

        Assert.Equal("somebody else's content", File.ReadAllText(file));
    }

    [Fact]
    public async Task Without_save_receipt_no_file_is_written_anywhere_and_the_receipt_is_only_on_the_terminal()
    {
        _backend.ValidTickets.Add(Ticket);
        WriteRecord();
        var before = Directory.GetFiles(_dir, "*", SearchOption.AllDirectories).Length;

        var r = await Submit(string.Empty, ["--yes"], ("EXIT_INTERVIEW_TICKET", Ticket));

        Assert.Equal(0, r.Code);
        Assert.Equal(before, Directory.GetFiles(_dir, "*", SearchOption.AllDirectories).Length);
        Assert.Contains("Not saved anywhere", r.Out);
    }

    // ---- delete-receipt ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_receipt_sends_one_header_and_words_the_answer_as_if_it_existed()
    {
        var code = FakeSubmissionBackend.NewReceiptCode();

        var r = await CliRun.RunAsync(["delete-receipt", "--server", Server], string.Empty, CliRun.Env(("EXIT_INTERVIEW_RECEIPT_CODE", code)));

        Assert.Equal(0, r.Code);
        var request = Assert.Single(_backend.Requests);
        Assert.Equal("DELETE", request.Method);
        Assert.Equal(FakeSubmissionBackend.ReceiptsPath, request.PathAndQuery);
        Assert.Empty(request.Body);
        Assert.Equal(code, request.Header("X-Receipt-Code"));
        Assert.Equal(["X-Receipt-Code"], request.Headers.Keys.Where(k => k.StartsWith("X-", StringComparison.OrdinalIgnoreCase)).ToList());
        Assert.Contains("If a record with this receipt code existed, it is deleted now.", r.Out);
        Assert.Contains("does not confirm that one existed", r.Out);
        Assert.DoesNotContain(code, r.All);
    }

    [Fact]
    public async Task Delete_receipt_reads_the_code_from_stdin_or_a_hidden_prompt()
    {
        var code = FakeSubmissionBackend.NewReceiptCode();
        var prompt = new FakePrompt(code);

        var piped = await CliRun.RunAsync(["delete-receipt", "--server", Server], code + "\n", CliRun.Env());
        var typed = await CliRun.RunAsync(["delete-receipt", "--server", Server], string.Empty, CliRun.Env(), prompt: prompt);
        var nothing = await CliRun.RunAsync(["delete-receipt", "--server", Server], string.Empty, CliRun.Env());

        Assert.Equal(0, piped.Code);
        Assert.Equal(0, typed.Code);
        Assert.Equal(1, prompt.Asked);
        Assert.Equal(3, nothing.Code);
        Assert.Equal(2, _backend.Requests.Count);
        Assert.DoesNotContain(code, piped.All + typed.All);
    }

    [Fact]
    public async Task A_malformed_receipt_code_is_a_visible_error_not_a_false_deleted()
    {
        var r = await CliRun.RunAsync(["delete-receipt", "--server", Server], "ABCDEFGHIJKLMNOPQRSTUVWXYZ\n", CliRun.Env());

        Assert.Equal(5, r.Code);
        Assert.Contains("INVALID_RECEIPT_CODE", r.Err);
        Assert.Contains("not a well-formed receipt code", r.Err);
        Assert.DoesNotContain("deleted now", r.Out);
        Assert.DoesNotContain("ABCDEFGHIJKLMNOPQRSTUVWXYZ", r.All);
    }

    [Fact]
    public async Task A_receipt_deletion_follows_no_redirect_and_survives_a_rate_limit_message()
    {
        using var elsewhere = new FakeSubmissionBackend();
        _backend.Script = _ => new FakeReply(307, Headers: new Dictionary<string, string> { ["Location"] = elsewhere.BaseUrl + "api/v1/receipts" });

        var r = await CliRun.RunAsync(["delete-receipt", "--server", Server], FakeSubmissionBackend.NewReceiptCode() + "\n", CliRun.Env());

        Assert.Equal(6, r.Code);
        Assert.Empty(elsewhere.Requests);
        Assert.Contains("Deleting is safe to repeat", r.Err + string.Join(' ', SubmitMessages.Explain(new CallResult.Failed(Failure.TimedOut), ServerUrl.Parse(Server), false)));
    }

    // ---- canaries over every path ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Canary_secrets_and_submitted_text_appear_only_where_they_are_meant_to_across_every_outcome()
    {
        var outputs = new List<CliResult>();
        var env = new[] { ("EXIT_INTERVIEW_TICKET", CliRun.CanaryTicket) };

        _backend.ValidTickets.Add(CliRun.CanaryTicket);
        outputs.Add(await Submit(string.Empty, ["--yes"], env));                               // 201 (ticket now used)
        outputs.Add(await Submit(string.Empty, ["--yes"], env));                               // 401
        _backend.Script = _ => FakeSubmissionBackend.Problem(422, "PII_DETECTED", kinds: ["Email"]);
        outputs.Add(await Submit(string.Empty, ["--yes"], env));                               // 422
        _backend.Script = _ => FakeSubmissionBackend.RateLimited();
        outputs.Add(await Submit(string.Empty, ["--yes"], env));                               // 429
        _backend.Script = _ => new FakeReply(500, "CANARYTICKET-0123456789-abcdefghijklmnopqrstuvw in a body");
        outputs.Add(await Submit(string.Empty, ["--yes"], env));                               // 500 echoing garbage
        outputs.Add(await CliRun.RunAsync(["submit", "--record", WriteRecord(), "--server", $"http://127.0.0.1:{FakeSubmissionBackend.FreePort()}", "--yes"], string.Empty, CliRun.Env(env))); // refused
        outputs.Add(await CliRun.RunAsync(["submit", "--record", WriteRecord(), "--server", "https://u:CANARYTICKET-0123456789-abcdefghijklmnopqrstuvw@x.example/?t=1", "--yes"], string.Empty, CliRun.Env(env)));
        outputs.Add(await Submit("CANARYTICKET-0123456789-abcdefghijklmnopqrstuvw\n"));          // no confirmation

        foreach (var r in outputs) Assert.DoesNotContain(CliRun.CanaryTicket, r.All);
        foreach (var r in outputs.Skip(1)) Assert.DoesNotContain("== Your receipt code", r.All);
        // The quote is shown in the preview (that is the point) but never in an error line.
        foreach (var r in outputs) Assert.DoesNotContain(TestRecord.CanaryQuote, r.Err);
        Assert.DoesNotContain(Directory.GetFiles(_dir, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith("record.json")), f => File.ReadAllText(f).Contains("CANARY"));
    }

    [Fact]
    public void Telemetry_export_subscribes_to_no_http_source_so_a_secret_header_or_url_cannot_become_a_span_or_metric()
    {
        // The submission client is not instrumented and the exporters listen to named sources only. A System.Net.Http source or meter
        // (or an Http instrumentation package) would record request URLs; this fails if one is ever added.
        Assert.DoesNotContain(TelemetrySetup.Sources.Concat(TelemetrySetup.Meters), n => n.StartsWith("System.Net", StringComparison.Ordinal) || n.Contains("Http", StringComparison.OrdinalIgnoreCase));
        var csproj = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../src/ExitInterviewAgent.Cli/ExitInterviewAgent.Cli.csproj"));
        Assert.DoesNotContain("Instrumentation.Http", csproj);
    }

    [Fact]
    public void A_secret_cannot_be_formatted_serialised_or_inspected_into_the_open()
    {
        using var secret = Secret(CliRun.CanaryTicket);

        Assert.Equal("[redacted]", secret.ToString());
        Assert.Equal("[redacted]", $"{secret}");
        Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(secret));
        Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(new { secret }));
        Assert.DoesNotContain("CANARY", new System.Diagnostics.DebuggerDisplayAttribute("[redacted]").Value);
        Assert.False(RedactedSecret.TryCreate("short", out _));
        Assert.False(RedactedSecret.TryCreate("has a space in it, so not a token", out _));
        Assert.False(RedactedSecret.TryCreate(new string('a', 300), out _));
    }
}
