using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using ExitInterviewAgent.Agent.Protocol;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ExitInterviewAgent.Providers;

/// <summary>
/// The factory entry point: <see cref="ProviderSettings"/> in, a metered, budgeted, instrumented <see cref="IChatClient"/> out.
/// T7 builds a real-model profile with <c>ProviderConfigResolver.Resolve(...)</c> (or a <see cref="ProviderSettings"/> literal) and this method.
/// The scripted mock is not created here: it is not a provider (<c>ScriptedChatClient</c> in the Agent project).
/// </summary>
public static class ProviderChatClients
{
    /// <summary>Margin so that an SDK's own timeout never fires before this project's (which carry the right error).</summary>
    private static readonly TimeSpan SdkTimeoutMargin = TimeSpan.FromSeconds(10);

    public static ProviderChatClient Create(ProviderSettings settings, ProviderRuntime? runtime = null, ProtocolLimits? limits = null)
    {
        settings = settings.Validated();
        runtime ??= new ProviderRuntime();
        var budget = runtime.Budget ?? InterviewBudget.ForProtocol(limits ?? InterviewProtocol.Current.Limits, settings);
        var key = settings.ApiKey?.Reveal();
        var sdkTimeout = settings.Resilience.OverallTimeout + SdkTimeoutMargin;

        switch (settings.Kind)
        {
            case ProviderKind.Anthropic:
                {
                    var http = ProviderHttp.CreateClient(settings.Kind, settings.Resilience, AuthScheme.XApiKey, runtime);
                    if (settings.WorkspaceId is { } workspaceId) http.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-workspace-id", workspaceId);
                    // MaxRetries = 0: retries belong to the transport policy. ApiKey is explicit, so no environment or profile credential is resolved.
                    var client = new AnthropicClient { ApiKey = key!, BaseUrl = settings.BaseUrl.AbsoluteUri.TrimEnd('/'), HttpClient = http, MaxRetries = 0, Timeout = sdkTimeout };
                    // Current Claude models reject non-default sampling values with HTTP 400, so none is sent.
                    return new ProviderChatClient(new SamplingStrippedChatClient(client.AsIChatClient(settings.Model, 1024)), settings, runtime, budget, http);
                }
            case ProviderKind.OpenAiCompatible:
                {
                    var http = ProviderHttp.CreateClient(settings.Kind, settings.Resilience, key is null ? AuthScheme.None : AuthScheme.Bearer, runtime);
                    var options = new OpenAIClientOptions
                    {
                        Endpoint = settings.BaseUrl,
                        Transport = new HttpClientPipelineTransport(http),
                        RetryPolicy = new ClientRetryPolicy(0),
                        NetworkTimeout = sdkTimeout,
                    };
                    // The SDK insists on a credential object; with no key a placeholder is used and the transport removes the header.
                    var chat = new OpenAIClient(new ApiKeyCredential(key ?? "unused"), options).GetChatClient(settings.Model).AsIChatClient();
                    return new ProviderChatClient(chat, settings, runtime, budget, http);
                }
            case ProviderKind.Ollama:
                {
                    var http = ProviderHttp.CreateClient(settings.Kind, settings.Resilience, key is null ? AuthScheme.None : AuthScheme.Bearer, runtime);
                    if (key is not null) http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + key);
                    return new ProviderChatClient(new OllamaChatClient(http, settings.BaseUrl, settings.Model, settings.NumCtx), settings, runtime, budget, http);
                }
            default:
                throw new ArgumentException("The mock model is not a provider; use ScriptedChatClient.", nameof(settings));
        }
    }
}
