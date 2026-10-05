using ExitInterviewAgent.Cli.Tests.Support;
using ExitInterviewAgent.Providers;

namespace ExitInterviewAgent.Cli.Tests;

/// <summary>The submit step at the end of <c>interview</c>: offered, never automatic.</summary>
[Collection("Console")]
public sealed class InterviewSubmitOfferTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eia-offer-").FullName;
    private readonly FakeSubmissionBackend _backend = new();

    public void Dispose()
    {
        _backend.Dispose();
        Directory.Delete(_dir, true);
    }

    private static readonly string[] Mock = ["interview", "--provider", "mock", "--model", "scripted", "--tenure", "1y_3y", "--employer", "acme-example"];

    private Func<string, string?> Env(params (string, string)[] extra) =>
        CliRun.Env([(EnvVars.ConfigDir, Path.Combine(_dir, "cfg")), .. extra]);

    /// <summary>Serves the interview answers, and once the submit confirmation has been asked, the lines that answer it.</summary>
    private sealed class PhasedReader(StringWriter output, string answers, params string[] afterConfirmPrompt) : TextReader
    {
        private readonly Queue<string> _answers = new(answers.Split('\n'));
        private readonly Queue<string> _later = new(afterConfirmPrompt);

        public override string? ReadLine() =>
            output.ToString().Contains("Type \"submit\"", StringComparison.Ordinal) ? _later.TryDequeue(out var l) ? l : null : _answers.TryDequeue(out var a) ? a : null;

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => ValueTask.FromResult(ReadLine());
        public override Task<string?> ReadLineAsync() => Task.FromResult(ReadLine());
    }

    [Fact]
    public async Task A_finished_interview_with_a_server_offers_the_submit_step_shows_the_record_and_submits_on_the_typed_word()
    {
        _backend.ValidTickets.Add(CliRun.CanaryTicket);

        var r = await CliRun.RunAsync([.. Mock, "--server", _backend.BaseUrl.GetLeftPart(UriPartial.Authority)], string.Empty, Env(), readerFactory: o => new PhasedReader(o, InterviewCliTests.Answers(), "submit", CliRun.CanaryTicket));

        Assert.Equal(0, r.Code);
        Assert.Contains("== Submit? ==", r.Out);
        Assert.Contains("== The record that will be sent ==", r.Out);
        Assert.Contains("== Your receipt code (shown once) ==", r.Out);
        var sent = Assert.Single(_backend.Requests);
        Assert.Equal(CliRun.CanaryTicket, sent.Header("X-Submission-Ticket"));
        Assert.Contains("\"employerRef\": \"acme-example\"", System.Text.Encoding.UTF8.GetString(sent.Body));
        Assert.DoesNotContain(CliRun.CanaryTicket, r.All);
    }

    [Fact]
    public async Task Never_automatic_declining_or_running_out_of_input_at_the_confirmation_submits_nothing_and_the_interview_still_succeeds()
    {
        var url = _backend.BaseUrl.GetLeftPart(UriPartial.Authority);

        // The interview answers are all there is: the confirmation reads a fallback answer line, or end of input.
        var plain = await CliRun.RunAsync([.. Mock, "--server", url], InterviewCliTests.Answers(), Env());
        var declined = await CliRun.RunAsync([.. Mock, "--server", url], string.Empty, Env(("EXIT_INTERVIEW_TICKET", CliRun.CanaryTicket)), readerFactory: o => new PhasedReader(o, InterviewCliTests.Answers(), "no", CliRun.CanaryTicket));

        Assert.Equal(0, plain.Code);
        Assert.Equal(0, declined.Code);
        Assert.Contains("Not confirmed. Nothing was sent.", declined.Out);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task A_server_rejection_after_the_interview_is_reported_with_the_submit_exit_code_and_the_record_file_stays()
    {
        _backend.Script = _ => FakeSubmissionBackend.Problem(401, "TICKET_INVALID");
        var outDir = Path.Combine(_dir, "out");

        var r = await CliRun.RunAsync([.. Mock, "--out", outDir, "--server", _backend.BaseUrl.GetLeftPart(UriPartial.Authority)], string.Empty, Env(), readerFactory: o => new PhasedReader(o, InterviewCliTests.Answers(), "submit", CliRun.CanaryTicket));

        Assert.Equal(5, r.Code);
        Assert.Contains("Mint a new ticket", r.Err);
        Assert.True(File.Exists(Path.Combine(outDir, "record.json")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(outDir, "record.json")), Assert.Single(_backend.Requests).Body); // the file is exactly what was sent
        Assert.DoesNotContain(CliRun.CanaryTicket, r.All);
    }

    [Fact]
    public async Task Without_a_server_the_interview_only_says_how_to_submit_and_a_bad_server_address_is_refused_before_the_interview()
    {
        var plain = await CliRun.RunAsync(Mock, InterviewCliTests.Answers(), Env());
        var bad = await CliRun.RunAsync([.. Mock, "--server", "http://example.com"], InterviewCliTests.Answers(), Env());

        Assert.Equal(0, plain.Code);
        Assert.Contains("Submitting is optional and separate", plain.Out);
        Assert.DoesNotContain("== Submit? ==", plain.Out);
        Assert.Equal(2, bad.Code);
        Assert.DoesNotContain("Interviewer:", bad.Out);
    }
}
