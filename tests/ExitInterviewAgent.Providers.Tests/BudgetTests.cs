using System.Net;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Providers.Tests.Support;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Providers.Tests;

public class BudgetTests
{
    private static readonly ChatMessage[] Prompt = [new(ChatRole.User, "hello")];

    [Fact]
    public void The_hard_ceiling_is_derived_from_the_protocols_graceful_budget()
    {
        var limits = InterviewProtocol.Current.Limits;

        var snapshotBefore = InterviewBudget.ForProtocol(limits).Snapshot();
        var budget = InterviewBudget.ForProtocol(limits);
        budget.Record(limits.MaxEstimatedTokens * 2 - 1, 0, TimeSpan.Zero, false);
        budget.EnsureAvailable();
        budget.Record(1, 0, TimeSpan.Zero, false);

        Assert.Equal(0, snapshotBefore.Calls);
        Assert.Equal(ProviderFailureKind.BudgetExceeded, Assert.Throws<ProviderException>(budget.EnsureAvailable).Kind);
    }

    [Fact]
    public void A_user_token_budget_lowers_the_ceiling_and_the_call_cap_is_the_graceful_one_plus_the_extractor()
    {
        var limits = InterviewProtocol.Current.Limits;
        var settings = Settings.For(ProviderKind.Ollama) with { MaxTokens = 1000 };
        var budget = InterviewBudget.ForProtocol(limits, settings);

        budget.Record(2000, 0, TimeSpan.Zero, false);

        Assert.Throws<ProviderException>(budget.EnsureAvailable);
        var callsBudget = InterviewBudget.ForProtocol(limits);
        for (var i = 0; i < limits.MaxModelCalls + 4; i++) { callsBudget.EnsureAvailable(); callsBudget.Record(1, 1, TimeSpan.Zero, false); }
        var ex = Assert.Throws<ProviderException>(callsBudget.EnsureAvailable);
        Assert.Contains("calls", ex.Message);
    }

    [Fact]
    public void Cost_is_tokens_times_the_users_prices_and_a_cost_ceiling_stops_the_next_call()
    {
        var prices = new PriceConfig(2m, 10m, "USD");
        var budget = new InterviewBudget(10_000_000, 100, prices, maxCost: 0.05m);

        budget.Record(10_000, 1_000, TimeSpan.FromSeconds(1), false);
        var snapshot = budget.Snapshot();
        budget.EnsureAvailable();
        budget.Record(10_000, 1_000, TimeSpan.FromSeconds(3), false);

        Assert.Equal(0.03m, snapshot.Cost);
        Assert.Equal("USD", snapshot.Currency);
        Assert.Contains("cost", Assert.Throws<ProviderException>(budget.EnsureAvailable).Message);
        var total = budget.Snapshot();
        Assert.Equal((2, 20_000L, 2_000L), (total.Calls, total.InputTokens, total.OutputTokens));
        Assert.Equal(TimeSpan.FromSeconds(4), total.TotalLatency);
        Assert.Equal(TimeSpan.FromSeconds(3), total.MaxLatency);
    }

    [Fact]
    public void A_cost_ceiling_without_prices_is_a_programming_error()
    {
        Assert.Throws<ArgumentException>(() => new InterviewBudget(1000, 10, null, 1m));
    }

    [Fact]
    public async Task A_spent_budget_stops_the_next_call_before_any_request_is_made_and_the_error_is_fatal()
    {
        var backend = new FakeBackend(ProviderKind.Anthropic);
        using var client = ProviderChatClients.Create(Settings.For(ProviderKind.Anthropic), new ProviderRuntime { Transport = backend, Budget = new InterviewBudget(30, 100) });
        var longPrompt = new[] { new ChatMessage(ChatRole.User, new string('x', 400)) };

        await client.GetResponseAsync(longPrompt);
        var ex = await Assert.ThrowsAsync<ProviderException>(() => client.GetResponseAsync(longPrompt));

        Assert.Equal(ProviderFailureKind.BudgetExceeded, ex.Kind);
        Assert.True(ex.IsFatal);
        Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public async Task Usage_comes_from_the_providers_response_and_is_estimated_only_when_it_is_missing()
    {
        var backend = new FakeBackend(ProviderKind.Ollama);
        var budget = new InterviewBudget(1_000_000, 100);
        using var client = ProviderChatClients.Create(Settings.For(ProviderKind.Ollama), new ProviderRuntime { Transport = backend, Budget = budget });

        await client.GetResponseAsync(Prompt);
        var reported = budget.Snapshot();
        backend.Script = (_, _) => Task.FromResult<HttpResponseMessage?>(backend.Json(HttpStatusCode.OK, """{ "model": "m", "message": { "role": "assistant", "content": "twelve chars" }, "done": true }"""));
        await client.GetResponseAsync(Prompt);
        var withEstimate = budget.Snapshot();

        Assert.False(reported.AnyEstimated);
        Assert.True(withEstimate.AnyEstimated);
        Assert.Equal(2, withEstimate.Calls);
        Assert.True(withEstimate.InputTokens > reported.InputTokens);
    }
}
