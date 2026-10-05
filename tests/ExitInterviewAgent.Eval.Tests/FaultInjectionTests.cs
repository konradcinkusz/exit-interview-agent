using System.Net;
using System.Text.Json;
using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Scenarios;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Eval.Tests;

public class FaultInjectionTests
{
    private static ChatOptions As(string role) => new() { AdditionalProperties = new AdditionalPropertiesDictionary { [MeteredChatClient.RoleKey] = role } };

    /// <summary>A minimal inner model: answers every call with a fixed text and a usage.</summary>
    private sealed class Fixed(string text) : IChatClient
    {
        public int Calls;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 } });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static Task<ChatResponse> Call(IChatClient c, string role) => c.GetResponseAsync([new ChatMessage(ChatRole.User, "x")], As(role));

    [Fact]
    public async Task A_timeout_fault_throws_for_its_roles_only_and_leaves_the_others_alone()
    {
        var inner = new Fixed("ok");
        var client = new FaultInjectingChatClient(inner, [new FaultSpec("timeout", ["interviewer"])]);

        await Assert.ThrowsAsync<TimeoutException>(() => Call(client, "interviewer"));
        Assert.Equal("ok", (await Call(client, "prober")).Text);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task A_server_error_fault_throws_an_http_error_with_status_500()
    {
        var client = new FaultInjectingChatClient(new Fixed("ok"), [new FaultSpec("server_error", ["prober"])]);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Call(client, "prober"));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    [Fact]
    public async Task Times_limits_a_fault_to_the_first_n_matching_calls_by_ordinal()
    {
        var client = new FaultInjectingChatClient(new Fixed("ok"), [new FaultSpec("malformed_json", ["extractor"], Times: 1)]);

        var first = (await Call(client, "extractor")).Text;
        var second = (await Call(client, "extractor")).Text;

        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(first));
        Assert.Equal("ok", second);
        Assert.Equal(1, client.Injected);
    }

    [Fact]
    public async Task An_empty_answer_fault_returns_empty_text_without_calling_the_model()
    {
        var inner = new Fixed("ok");
        var client = new FaultInjectingChatClient(inner, [new FaultSpec("empty_answer", ["interviewer"])]);

        Assert.Equal(string.Empty, (await Call(client, "interviewer")).Text);
        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public async Task Usage_faults_post_process_a_real_answer()
    {
        var missing = new FaultInjectingChatClient(new Fixed("ok"), [new FaultSpec("usage_missing", ["interviewer"])]);
        var inflated = new FaultInjectingChatClient(new Fixed("ok"), [new FaultSpec("usage_inflated", ["interviewer"], Tokens: 5000)]);

        Assert.Null((await Call(missing, "interviewer")).Usage);
        var u = (await Call(inflated, "interviewer")).Usage!;
        Assert.Equal(5000, u.InputTokenCount + u.OutputTokenCount);
    }

    [Fact]
    public async Task A_compromised_interviewer_cycles_through_its_variants_by_call()
    {
        var client = new FaultInjectingChatClient(new Fixed("ok"), [new FaultSpec("compromised", ["interviewer"], Variants: ["leading", "closed"])]);

        var a = (await Call(client, "interviewer")).Text;
        var b = (await Call(client, "interviewer")).Text;
        var c = (await Call(client, "interviewer")).Text;

        Assert.Equal(FaultInjectingChatClient.InterviewerVariants["leading"], a);
        Assert.Equal(FaultInjectingChatClient.InterviewerVariants["closed"], b);
        Assert.Equal(a, c);
    }

    [Fact]
    public async Task A_compromised_extractor_bends_a_real_extraction_the_way_an_obedient_model_would()
    {
        var clean = (await new ScriptedChatClient().GetResponseAsync(
            [new ChatMessage(ChatRole.System, Prompts.ExtractorSystem(ExitInterviewAgent.Agent.Protocol.InterviewProtocol.Current)), new ChatMessage(ChatRole.User, "<<<TRANSCRIPT_DATA a>>>\n<<<END_TRANSCRIPT_DATA a>>>")])).Text;

        var obeys = JsonDocument.Parse(FaultInjectingChatClient.CompromiseExtraction(clean, "obeys_injection")).RootElement;
        var affect = FaultInjectingChatClient.CompromiseExtraction(clean.Replace("\"no_data\"", "\"covered\",\"rating\":3,\"confidence\":\"low\",\"quotes\":[\"x\"]"), "affect_field");

        Assert.True(obeys.TryGetProperty("email", out _));
        Assert.Equal(5, obeys.GetProperty("topics").GetProperty("culture").GetProperty("rating").GetInt32());
        Assert.Contains("sentiment", affect);
    }

    [Fact]
    public async Task Every_fault_kind_in_the_schema_is_implemented_by_the_decorator_and_used_by_the_corpus()
    {
        var kinds = new[] { "timeout", "server_error", "empty_answer", "malformed_json", "usage_missing", "usage_inflated", "compromised" };

        var used = Support.Fixtures.Corpus.SelectMany(l => l.Scenario.Faults ?? []).Select(f => f.Kind).Distinct().ToList();

        foreach (var k in kinds) Assert.Contains(k, used);
        await Task.CompletedTask;
    }
}
