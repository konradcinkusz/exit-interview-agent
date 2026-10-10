using System.Security.Cryptography;
using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Infrastructure.Auth;
using ExitInterviewAgent.InterviewService.Interviews;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ExitInterviewAgent.InterviewService.Tests.Interviews;

/// <summary>
/// The service in-process for the interview session tests. <see cref="ServiceFactory"/> is sealed, so this host repeats its
/// small setup (one throwaway key, the same issuer and audience) and adds what the sessions need: a fake clock, a log capture,
/// a credit-refund recorder, and an optional model that replaces the configured provider.
/// </summary>
public sealed class InterviewHost : WebApplicationFactory<Program>
{
    private readonly RSA _rsa = RSA.Create(2048);

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
    public CaptureLoggerProvider Logs { get; } = new();
    public RecordingRefund Refunds { get; } = new();

    /// <summary>Settings applied before the host builds. The default is the scripted mock in Polish-capable mode.</summary>
    public Dictionary<string, string?> Settings { get; } = new()
    {
        ["Interviews:Provider"] = "mock",
        ["Interviews:Model"] = "scripted",
    };

    /// <summary>When set, this model replaces the configured provider (used for the failure path).</summary>
    public IChatClient? ModelOverride { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:Issuer", ServiceFactory.Issuer);
        builder.UseSetting("Jwt:Audience", ServiceFactory.Audience);
        builder.UseSetting("Mcp:Issuer", ServiceFactory.McpIssuer);
        builder.UseSetting("Mcp:Resource", ServiceFactory.McpResource);
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }
        builder.ConfigureServices(services =>
        {
            var key = new RsaSecurityKey(_rsa) { KeyId = ServiceFactory.KeyId };
            foreach (var scheme in new[] { JwtBearerDefaults.AuthenticationScheme, "McpBearer" })
            {
                services.PostConfigure<JwtBearerOptions>(scheme, o => o.TokenValidationParameters.IssuerSigningKey = key);
            }
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ICreditRefund>(Refunds);
            services.AddLogging(l => l.AddProvider(Logs));
            if (ModelOverride is { } model)
            {
                services.AddSingleton<IInterviewModelFactory>(new FixedModelFactory(model));
            }
        });
    }

    /// <summary>An HTTP client that calls the API as the given account.</summary>
    public HttpClient As(string account)
    {
        var client = CreateClient();
        var token = new TokenBuilder(_rsa) { Subject = account, Issuer = ServiceFactory.Issuer, Audience = ServiceFactory.Audience, Type = "JWT" }.Build();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _rsa.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Records the refunds the sessions ask for, by session id.</summary>
    public sealed class RecordingRefund : ICreditRefund
    {
        private readonly List<string> _sessions = [];

        public IReadOnlyList<string> Sessions { get { lock (_sessions) return [.. _sessions]; } }

        public ValueTask RefundAsync(string accountId, string sessionId, CancellationToken ct)
        {
            lock (_sessions) _sessions.Add(sessionId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedModelFactory(IChatClient client) : IInterviewModelFactory
    {
        public InterviewModel Create() => new(client, null);
    }
}

/// <summary>The default test model, exposed so tests can state which model they expect (the scripted mock).</summary>
public static class TestModels
{
    public static IChatClient Scripted() => new ScriptedChatClient();
}

/// <summary>
/// A model that fails on every call with a FATAL failure (a rejected key, a spent budget): the agent stops the interview at the
/// first call instead of continuing on fallback wording (ADR-0034). Non-fatal failures are the agent's to absorb, not the service's.
/// </summary>
public sealed class FailingChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromException<ChatResponse>(new FatalModelFailure());

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>The failure a model client throws for a rejected key: a controlled code, no message, fatal.</summary>
public sealed class FatalModelFailure : Exception, IModelFailure
{
    public string FailureCode => "auth_rejected";

    public bool IsFatal => true;
}
