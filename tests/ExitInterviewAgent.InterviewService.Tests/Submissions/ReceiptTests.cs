using System.Diagnostics;
using System.Net;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Submissions;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.Submissions;

public sealed class ReceiptTests : IDisposable
{
    private readonly TestHost _host = new(new() { ["Submission:Receipts:ResponseFloorMilliseconds"] = "0" });

    public void Dispose() => _host.Dispose();

    private async Task<(string Sub, string Employer, string Id, string Code)> SubmitAsync()
    {
        var sub = TestRecords.NewSub();
        var record = TestRecords.Valid();
        var (response, body) = await _host.Client(_host.WebToken(sub)).PostAsync("/api/v1/submissions", TestRecords.Json(record)).ReadAsync();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (sub, record["employerRef"]!.GetValue<string>(), record["interviewId"]!.GetValue<string>(), TestRecords.ReceiptCodeOf(body));
    }

    private Task<HttpResponseMessage> DeleteAsync(string? code, TestHost? host = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/receipts");
        if (code is not null)
        {
            request.Headers.Add(SubmissionHeaders.ReceiptCode, code);
        }
        return (host ?? _host).Client().SendAsync(request);
    }

    private Task<(int Records, int Receipts)> CountsAsync() => _host.InScopeAsync(async sp =>
    {
        var db = sp.GetRequiredService<InterviewDbContext>();
        return (await db.Records.CountAsync(), await db.Receipts.CountAsync());
    });

    [Fact]
    public async Task The_code_deletes_the_record_and_its_receipt_without_any_account()
    {
        var submitted = await SubmitAsync();
        var before = await CountsAsync();

        var response = await DeleteAsync(submitted.Code);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await CountsAsync();
        Assert.Equal(before.Records - 1, after.Records);
        Assert.Equal(before.Receipts - 1, after.Receipts);
        Assert.False(await _host.InScopeAsync(async sp => await sp.GetRequiredService<InterviewDbContext>().Records.AnyAsync(r => r.Id == submitted.Id)));
    }

    [Fact]
    public async Task Deleting_the_record_does_not_clear_the_ledger_so_the_same_employer_stays_closed_to_that_account()
    {
        var submitted = await SubmitAsync();
        await DeleteAsync(submitted.Code);

        var again = TestRecords.Valid(submitted.Employer);
        var (response, body) = await _host.Client(_host.WebToken(submitted.Sub)).PostAsync("/api/v1/submissions", TestRecords.Json(again)).ReadAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SubmissionCodes.AlreadySubmitted, TestRecords.Code(body));
    }

    [Fact]
    public async Task Known_deleted_and_unknown_codes_get_the_same_answer()
    {
        var submitted = await SubmitAsync();

        var known = await DeleteAsync(submitted.Code);
        var again = await DeleteAsync(submitted.Code);
        var unknown = await DeleteAsync(ReceiptCodes.NewCode());

        foreach (var response in new[] { known, again, unknown })
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(known.Headers.Select(h => h.Key).Order(), response.Headers.Select(h => h.Key).Order());
        }
    }

    [Fact]
    public async Task A_deleting_an_unknown_code_changes_nothing()
    {
        await SubmitAsync();
        var before = await CountsAsync();

        await DeleteAsync(ReceiptCodes.NewCode());

        Assert.Equal(before, await CountsAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task A_malformed_code_is_a_400_with_a_stable_code_and_says_nothing_about_records(string? code)
    {
        var response = await DeleteAsync(code);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SubmissionCodes.InvalidReceiptCode, TestRecords.Code(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task A_code_in_the_url_is_not_accepted_secrets_travel_in_headers()
    {
        var submitted = await SubmitAsync();

        var inPath = await _host.Client().DeleteAsync($"/api/v1/receipts/{submitted.Code}");
        var inQuery = await _host.Client().DeleteAsync($"/api/v1/receipts?code={submitted.Code}");

        Assert.Equal(HttpStatusCode.NotFound, inPath.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inQuery.StatusCode);
        Assert.True(await _host.InScopeAsync(async sp => await sp.GetRequiredService<InterviewDbContext>().Records.AnyAsync(r => r.Id == submitted.Id)));
    }

    [Fact]
    public async Task Every_well_formed_request_takes_at_least_the_floor_whether_or_not_the_code_exists()
    {
        using var host = new TestHost(new() { ["Submission:Receipts:ResponseFloorMilliseconds"] = "300" });
        var sub = TestRecords.NewSub();
        var record = TestRecords.Valid();
        var (_, body) = await host.Client(host.WebToken(sub)).PostAsync("/api/v1/submissions", TestRecords.Json(record)).ReadAsync();
        var real = TestRecords.ReceiptCodeOf(body);

        var hit = Stopwatch.StartNew();
        await DeleteAsync(real, host);
        hit.Stop();
        var miss = Stopwatch.StartNew();
        await DeleteAsync(ReceiptCodes.NewCode(), host);
        miss.Stop();

        Assert.True(hit.ElapsedMilliseconds >= 290, $"hit took {hit.ElapsedMilliseconds} ms");
        Assert.True(miss.ElapsedMilliseconds >= 290, $"miss took {miss.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Deletion_is_rate_limited_per_client_and_globally()
    {
        using var perIp = new TestHost(new() { ["Submission:Receipts:ResponseFloorMilliseconds"] = "0", ["Submission:Limits:ReceiptDeletePerIpPerMinute"] = "3" });
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await DeleteAsync(ReceiptCodes.NewCode(), perIp)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[3]);

        using var global = new TestHost(new() { ["Submission:Receipts:ResponseFloorMilliseconds"] = "0", ["Submission:Limits:ReceiptDeleteGlobalPerMinute"] = "2", ["Submission:Limits:ReceiptDeletePerIpPerMinute"] = "100" });
        statuses.Clear();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await DeleteAsync(ReceiptCodes.NewCode(), global)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[2]);
    }
}
