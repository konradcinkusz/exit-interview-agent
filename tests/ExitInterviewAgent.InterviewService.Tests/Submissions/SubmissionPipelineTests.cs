using System.Net;
using System.Text;
using System.Text.Json;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Submissions;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.Submissions;

/// <summary>Each stage of the pipeline, through the real HTTP pipeline: size, schema, PII, AI disclosure, verification, ledger, persist.</summary>
public sealed class SubmissionPipelineTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private Task<(HttpResponseMessage Response, string Body)> SubmitAsync(string sub, byte[] body, TestHost? host = null)
    {
        var h = host ?? _host;
        return h.Client(h.WebToken(sub)).PostAsync("/api/v1/submissions", TestRecords.Json(body)).ReadAsync();
    }

    [Fact]
    public async Task A_valid_record_is_accepted_and_a_receipt_code_is_returned_once()
    {
        var sub = TestRecords.NewSub();
        var record = TestRecords.Valid();

        var (response, body) = await SubmitAsync(sub, TestRecords.Bytes(record));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var code = TestRecords.ReceiptCodeOf(body);
        Assert.True(ReceiptCodes.IsWellFormed(code));
        // The store holds the hash of the code and the record under its pseudonymous id; never the code itself.
        await _host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<InterviewDbContext>();
            var receipt = await db.Receipts.SingleAsync(r => r.RecordId == record["interviewId"]!.GetValue<string>());
            Assert.Equal(SecretTokens.Hash(code), receipt.CodeHash);
            Assert.DoesNotContain(code, receipt.CodeHash);
            var row = await db.Records.SingleAsync(r => r.Id == receipt.RecordId);
            Assert.Equal(record["employerRef"]!.GetValue<string>(), row.EmployerRef);
            Assert.DoesNotContain(sub, row.Json);
            return 0;
        });
    }

    [Fact]
    public async Task The_stored_record_is_the_canonical_form()
    {
        var record = TestRecords.Valid();
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(record))).Response.StatusCode);

        var json = await _host.InScopeAsync(async sp =>
            (await sp.GetRequiredService<InterviewDbContext>().Records.SingleAsync(r => r.Id == record["interviewId"]!.GetValue<string>())).Json);

        var canonical = ExitInterviewAgent.Records.RecordSerializer.SerializeCanonicalString(new ExitInterviewAgent.Records.RecordValidator().Validate(TestRecords.Bytes(record)).Record!);
        Assert.Equal(canonical, json);
    }

    [Fact]
    public async Task An_oversized_body_is_413_and_a_stable_code()
    {
        var huge = new byte[170 * 1024];
        Array.Fill(huge, (byte)' ');

        var (response, body) = await SubmitAsync(TestRecords.NewSub(), huge);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(SubmissionCodes.PayloadTooLarge, TestRecords.Code(body));
    }

    [Fact]
    public async Task A_chunked_oversized_body_without_a_content_length_is_still_refused()
    {
        var client = _host.Client(_host.WebToken(TestRecords.NewSub()));
        var content = new StreamContent(new MemoryStream(new byte[200 * 1024])); // no Content-Length: the stream length is unknown to the handler
        content.Headers.ContentLength = null;
        content.Headers.ContentType = new("application/json");

        var response = await client.PostAsync("/api/v1/submissions", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Theory]
    [InlineData("{ not json", "NOT_JSON", HttpStatusCode.BadRequest)]
    [InlineData("{\"schemaVersion\":\"1\"}", "MISSING_FIELD", HttpStatusCode.UnprocessableEntity)]
    public async Task A_schema_violation_is_a_problem_with_codes_and_paths_only(string json, string code, HttpStatusCode status)
    {
        var (response, body) = await SubmitAsync(TestRecords.NewSub(), Encoding.UTF8.GetBytes(json));

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, TestRecords.Code(body));
    }

    [Fact]
    public async Task Problem_details_never_echo_submitted_text()
    {
        const string canary = "zzcanaryquote";
        var record = TestRecords.Valid(change: r => { r["unexpectedField" + canary] = canary; r.SetQuotes("culture", canary); });

        var (response, body) = await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(record));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("UNKNOWN_FIELD", TestRecords.Code(body));
        Assert.DoesNotContain(canary, body);
    }

    [Theory]
    [InlineData("You can reach me at jane.doe@example.com for details.", "Email")]
    [InlineData("Call 600 700 800 if you want the story.", "Phone")]
    [InlineData("My boss named Piotr Kowalski shouted at everyone.", "PersonName")]
    public async Task Personal_data_the_client_failed_to_mask_is_rejected_with_kinds_and_no_content(string quote, string kind)
    {
        var record = TestRecords.Valid(change: r => r.SetQuotes("culture", quote));

        var (response, body) = await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(record));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(SubmissionCodes.PiiDetected, TestRecords.Code(body));
        Assert.Contains(kind, JsonDocument.Parse(body).RootElement.GetProperty("kinds").EnumerateArray().Select(k => k.GetString()));
        Assert.DoesNotContain("jane", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kowalski", body);
        Assert.DoesNotContain("600", body);
        Assert.False(await StoredAnyAsync(_host, record));
    }

    [Fact]
    public async Task Pii_in_any_topic_is_found_not_only_in_the_first()
    {
        var record = TestRecords.Valid(change: r => r.SetQuotes("reason_for_leaving", "Write to me at someone@example.org later."));

        var (response, body) = await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(record));

        Assert.Equal(SubmissionCodes.PiiDetected, TestRecords.Code(body));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Masked_placeholders_are_not_personal_data()
    {
        var record = TestRecords.Valid(change: r => r.SetQuotes("management", "[PERSON] told us to email [EMAIL] or call [PHONE]."));

        var (response, _) = await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(record));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_detector_failure_rejects_the_submission_and_stores_nothing()
    {
        const string canary = "zzdetectorcanary";
        using var host = new TestHost(services: s => s.AddSingleton<IPiiScanner>(new ThrowingScanner(canary)));
        var record = TestRecords.Valid();

        var (response, body) = await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(record), host);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(SubmissionCodes.PiiCheckFailed, TestRecords.Code(body));
        Assert.DoesNotContain(canary, body);
        Assert.False(await StoredAnyAsync(host, record));
    }

    [Fact]
    public async Task The_real_scanner_fails_closed_when_it_exceeds_its_time_budget()
    {
        using var host = new TestHost(settings: new() { ["Submission:Pii:TimeoutMilliseconds"] = "0" });

        var (_, body) = await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(TestRecords.Valid()), host);

        Assert.Equal(SubmissionCodes.PiiCheckFailed, TestRecords.Code(body));
    }

    [Fact]
    public async Task A_record_of_an_interview_without_ai_disclosure_is_rejected()
    {
        var record = TestRecords.Valid(change: r => r["interview"]!["aiDisclosed"] = false);

        var (response, body) = await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(record));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(SubmissionCodes.AiNotDisclosed, TestRecords.Code(body));
        Assert.False(await StoredAnyAsync(_host, record));
    }

    [Fact]
    public async Task A_record_with_pii_masked_false_is_rejected_by_the_library_check()
    {
        var record = TestRecords.Valid(change: r => r["piiMasked"] = false);

        var (_, body) = await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(record));

        Assert.Equal("PII_NOT_MASKED", TestRecords.Code(body));
    }

    [Theory]
    [InlineData("verified", VerificationLevel.Verified)]
    [InlineData("unverified", VerificationLevel.Unverified)]
    [InlineData("unavailable", VerificationLevel.Unchecked)]
    public async Task The_verification_level_is_kept_in_the_store_row_and_an_unavailable_verifier_does_not_break_submission(string mode, VerificationLevel expected)
    {
        using var host = new TestHost(settings: new() { ["Submission:Verification:MockMode"] = mode });
        var record = TestRecords.Valid();

        var (response, _) = await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(record), host);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var level = await host.InScopeAsync(async sp =>
            (await sp.GetRequiredService<InterviewDbContext>().Records.SingleAsync(r => r.Id == record["interviewId"]!.GetValue<string>())).Verification);
        Assert.Equal(expected, level);
    }

    [Fact]
    public async Task A_verifier_that_throws_or_hangs_degrades_to_unchecked()
    {
        foreach (var verifier in new IEmploymentVerifier[] { new FaultyVerifier(hang: false), new FaultyVerifier(hang: true) })
        {
            using var host = new TestHost(settings: new() { ["Submission:Verification:TimeoutMilliseconds"] = "100" }, services: s => s.AddSingleton(verifier));
            var record = TestRecords.Valid();

            var (response, _) = await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(record), host);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(VerificationLevel.Unchecked, await host.InScopeAsync(async sp =>
                (await sp.GetRequiredService<InterviewDbContext>().Records.SingleAsync(r => r.Id == record["interviewId"]!.GetValue<string>())).Verification));
        }
    }

    [Fact]
    public async Task The_strict_policy_rejects_a_claim_the_verifier_could_not_confirm_but_not_an_unavailable_verifier()
    {
        using var strict = new TestHost(settings: new() { ["Submission:Verification:RejectUnverified"] = "true" });
        var (response, body) = await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(TestRecords.Valid()), strict);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(SubmissionCodes.EmploymentNotVerified, TestRecords.Code(body));

        using var degraded = new TestHost(settings: new() { ["Submission:Verification:RejectUnverified"] = "true", ["Submission:Verification:MockMode"] = "unavailable" });
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(TestRecords.Valid()), degraded)).Response.StatusCode);
    }

    [Fact]
    public async Task One_submission_per_employer_per_account_but_other_employers_and_other_accounts_are_free()
    {
        var sub = TestRecords.NewSub();
        var employer = TestRecords.NewEmployer();

        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(sub, TestRecords.Bytes(TestRecords.Valid(employer)))).Response.StatusCode);
        var (second, body) = await SubmitAsync(sub, TestRecords.Bytes(TestRecords.Valid(employer)));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(SubmissionCodes.AlreadySubmitted, TestRecords.Code(body));
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(sub, TestRecords.Bytes(TestRecords.Valid()))).Response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(TestRecords.Valid(employer)))).Response.StatusCode);
    }

    [Fact]
    public async Task A_rejected_duplicate_stores_nothing()
    {
        var sub = TestRecords.NewSub();
        var employer = TestRecords.NewEmployer();
        await SubmitAsync(sub, TestRecords.Bytes(TestRecords.Valid(employer)));
        var duplicate = TestRecords.Valid(employer);

        await SubmitAsync(sub, TestRecords.Bytes(duplicate));

        Assert.False(await StoredAnyAsync(_host, duplicate));
    }

    [Fact]
    public async Task A_taken_interview_id_is_refused_and_leaves_no_ledger_entry_behind()
    {
        var record = TestRecords.Valid();
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(record))).Response.StatusCode);
        var before = await _host.InScopeAsync(async sp => await sp.GetRequiredService<InterviewDbContext>().SubmissionLedger.CountAsync());

        var clash = TestRecords.Valid(change: r => r["interviewId"] = record["interviewId"]!.GetValue<string>());
        var (response, body) = await SubmitAsync(TestRecords.NewSub(), TestRecords.Bytes(clash));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SubmissionCodes.InterviewIdTaken, TestRecords.Code(body));
        Assert.Equal(before, await _host.InScopeAsync(async sp => await sp.GetRequiredService<InterviewDbContext>().SubmissionLedger.CountAsync()));
    }

    private static async Task<bool> StoredAnyAsync(TestHost host, System.Text.Json.Nodes.JsonObject record) =>
        await host.InScopeAsync(async sp =>
        {
            var id = record["interviewId"]!.GetValue<string>();
            return await sp.GetRequiredService<InterviewDbContext>().Records.AnyAsync(r => r.Id == id);
        });

    private sealed class ThrowingScanner(string canary) : IPiiScanner
    {
        public Task<PiiScanResult> ScanAsync(ExitInterviewAgent.Records.InterviewRecord record, CancellationToken ct) =>
            throw new InvalidOperationException("scanner exploded on " + canary);
    }

    private sealed class FaultyVerifier(bool hang) : IEmploymentVerifier
    {
        public async Task<EmploymentCheck> VerifyAsync(string subject, string employerRef, CancellationToken ct)
        {
            if (hang)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            throw new HttpRequestException("registry down for " + employerRef);
        }
    }
}
