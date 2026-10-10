using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Interviews;

/// <summary>A model for one session. <see cref="Lease"/> is disposed when the session ends (a provider client owns a budget and a connection).</summary>
public sealed record InterviewModel(IChatClient Client, IDisposable? Lease) : IDisposable
{
    public void Dispose() => Lease?.Dispose();
}

/// <summary>Thrown when no provider is configured or the configured one cannot be built. The endpoints answer 503.</summary>
public sealed class InterviewUnavailableException : Exception
{
    public InterviewUnavailableException() : base("No interview provider is available.")
    {
    }
}

public interface IInterviewModelFactory
{
    /// <exception cref="InterviewUnavailableException">No provider is configured, or it cannot be built.</exception>
    InterviewModel Create();
}

/// <summary>
/// Builds the model from <c>Interviews:*</c> and the environment, through the CLI's own resolver so that both read the same
/// variables and the same rules. The user config file is not read here: the service has no per-user files.
/// </summary>
public sealed class ConfiguredInterviewModelFactory(IOptionsMonitor<InterviewServiceOptions> options) : IInterviewModelFactory
{
    public InterviewModel Create()
    {
        var o = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(o.Provider)) throw new InterviewUnavailableException();

        try
        {
            var resolved = ProviderConfigResolver.Resolve(
                new ProviderCliOptions { Provider = o.Provider, Model = o.Model, BaseUrl = o.BaseUrl, ApiKeyEnv = o.ApiKeyEnv },
                Environment.GetEnvironmentVariable);
            if (resolved.Settings.Kind == ProviderKind.Mock) return new InterviewModel(new ScriptedChatClient(), null);

            var client = ProviderChatClients.Create(resolved.Settings, null, InterviewProtocol.Current.Limits);
            return new InterviewModel(client, client);
        }
        catch (ProviderConfigurationException)
        {
            throw new InterviewUnavailableException();
        }
    }
}
