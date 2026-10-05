using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Cli;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.Submissions;

/// <summary>
/// The whole path, with nothing faked on the server side: a web token mints a ticket on the real service, the real CLI (in-process, with a
/// handler that reaches the test host instead of a socket) submits a record file with that ticket, shows a receipt code once, and
/// <c>delete-receipt</c> removes the record. This is also the test that notices when the CLI's fake backend (Cli.Tests) drifts from the contract.
/// </summary>
public sealed partial class CliEndToEndTests : IDisposable
{
    private const string Server = "http://localhost";
    private readonly TestHost _host = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("eia-e2e-").FullName;

    public void Dispose()
    {
        _host.Dispose();
        Directory.Delete(_dir, true);
    }

    [GeneratedRegex("[A-Za-z0-9_-]{46}")]
    private static partial Regex ReceiptShape();

    private async Task<string> MintAsync(string sub)
    {
        var (response, body) = await _host.Client(_host.WebToken(sub)).PostAsync("/api/v1/tickets", null).ReadAsync();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return TestRecords.TicketOf(body).Ticket;
    }

    private async Task<(int Code, string Out, string Err)> CliAsync(string[] args, string stdin, params (string, string)[] env)
    {
        var vars = env.ToDictionary(e => e.Item1, e => e.Item2);
        var o = new StringWriter();
        var e = new StringWriter();
        var code = await CliApp.RunAsync(args, new CliHost(new StringReader(stdin), o, e, k => vars.GetValueOrDefault(k), null, default, ExportTelemetry: false, Http: _host.Handler()));
        return (code, o.ToString(), e.ToString());
    }

    private string WriteRecord(string employer)
    {
        var path = Path.Combine(_dir, employer + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(JsonDocument.Parse(TestRecords.Bytes(TestRecords.Valid(employer))).RootElement, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return path;
    }

    private Task<int> CountRecordsAsync(string employer) =>
        _host.InScopeAsync(async sp => await sp.GetRequiredService<InterviewDbContext>().Records.CountAsync(r => r.EmployerRef == employer));

    [Fact]
    public async Task Mint_then_cli_submit_then_receipt_then_delete_receipt_against_the_real_service()
    {
        var employer = TestRecords.NewEmployer();
        var sub = TestRecords.NewSub();
        var ticket = await MintAsync(sub);
        var path = WriteRecord(employer);

        // submit: the ticket arrives from the environment, the confirmation is typed.
        var submit = await CliAsync(["submit", "--record", path, "--server", Server], "submit\n", ("EXIT_INTERVIEW_TICKET", ticket));

        Assert.True(submit.Code == 0, submit.Out + submit.Err);
        var receipt = Assert.Single(ReceiptShape().Matches(submit.Out.Split("== Your receipt code (shown once) ==")[1].Split("This is the only way")[0])).Value;
        Assert.True(ExitInterviewAgent.InterviewService.Submissions.ReceiptCodes.IsWellFormed(receipt));
        Assert.DoesNotContain(ticket, submit.Out + submit.Err);
        Assert.Equal(1, await CountRecordsAsync(employer));

        // The ticket is spent: the service answers TICKET_INVALID and the CLI says to mint a new one.
        var again = await CliAsync(["submit", "--record", WriteRecord(TestRecords.NewEmployer()), "--server", Server, "--yes"], string.Empty, ("EXIT_INTERVIEW_TICKET", ticket));
        Assert.Equal(5, again.Code);
        Assert.Contains("TICKET_INVALID", again.Err);
        Assert.Contains("Mint a new ticket", again.Err);

        // delete-receipt, code on stdin (not argv): 204, honest wording, and the record is gone.
        var delete = await CliAsync(["delete-receipt", "--server", Server], receipt + "\n");
        Assert.True(delete.Code == 0, delete.Out + delete.Err);
        Assert.Contains("If a record with this receipt code existed, it is deleted now.", delete.Out);
        Assert.Equal(0, await CountRecordsAsync(employer));

        // A well-formed code that matches nothing gets the same answer; a malformed one is a visible error.
        var unknown = await CliAsync(["delete-receipt", "--server", Server], ExitInterviewAgent.InterviewService.Submissions.ReceiptCodes.NewCode() + "\n");
        Assert.Equal(0, unknown.Code);
        var typo = await CliAsync(["delete-receipt", "--server", Server], receipt[..45] + (receipt[45] == 'A' ? 'B' : 'A') + "\n");
        Assert.Equal(5, typo.Code);
        Assert.Contains("INVALID_RECEIPT_CODE", typo.Err);
    }

    [Fact]
    public async Task The_service_refuses_what_the_cli_reports_and_the_wording_matches_the_codes_the_service_really_sends()
    {
        var sub = TestRecords.NewSub();
        var employer = TestRecords.NewEmployer();

        // A second submission for the same account and employer.
        var first = await CliAsync(["submit", "--record", WriteRecord(employer), "--server", Server, "--yes"], string.Empty, ("EXIT_INTERVIEW_TICKET", await MintAsync(sub)));
        Assert.Equal(0, first.Code);
        var duplicate = await CliAsync(["submit", "--record", WriteRecord(employer + "x"), "--server", Server, "--yes"], string.Empty, ("EXIT_INTERVIEW_TICKET", await MintAsync(sub)));
        // (a different employer is a different ledger entry: accepted)
        Assert.Equal(0, duplicate.Code);

        // Same account, same employer: a different record file with the same employer reference.
        var path = Path.Combine(_dir, "again.json");
        File.WriteAllBytes(path, TestRecords.Bytes(TestRecords.Valid(employer)));
        var already = await CliAsync(["submit", "--record", path, "--server", Server, "--yes"], string.Empty, ("EXIT_INTERVIEW_TICKET", await MintAsync(sub)));
        Assert.Equal(5, already.Code);
        Assert.Contains("ALREADY_SUBMITTED", already.Err);
        Assert.Contains("already submitted a record for this employer", already.Err);
    }
}
