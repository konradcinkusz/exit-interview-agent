using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Runner;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Agent.Roles;

/// <summary>Interviewer role on any <see cref="IChatClient"/> (use the metered one the runner builds).</summary>
public sealed class ModelInterviewer(IChatClient client, InterviewProtocol protocol) : IInterviewer
{
    public async Task<string> AskAsync(QuestionRequest request, CancellationToken ct)
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, Prompts.InterviewerSystem(request.Wording ?? protocol)),
            new ChatMessage(ChatRole.User, Prompts.InterviewerUser(request, DataBlock.NewNonce())),
        };
        var response = await client.GetResponseAsync(messages, MeteredChatClient.Options(Role.Interviewer, 160), ct).ConfigureAwait(false);
        return response.Text;
    }
}

/// <summary>Prober role: words the one concrete-example follow-up.</summary>
public sealed class ModelProber(IChatClient client, InterviewProtocol protocol) : IProber
{
    public async Task<string> ProbeAsync(QuestionRequest request, CancellationToken ct)
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, Prompts.ProberSystem(request.Wording ?? protocol)),
            new ChatMessage(ChatRole.User, Prompts.ProberUser(request, DataBlock.NewNonce())),
        };
        var response = await client.GetResponseAsync(messages, MeteredChatClient.Options(Role.Prober, 120), ct).ConfigureAwait(false);
        return response.Text;
    }
}

/// <summary>Record extractor role. Sees only the masked transcript, inside the data block, under its own system prompt.</summary>
public sealed class ModelRecordExtractor(IChatClient client, InterviewProtocol protocol) : IRecordExtractor
{
    public async Task<string> ExtractAsync(Transcript maskedTranscript, IReadOnlyList<string> previousErrorCodes, CancellationToken ct)
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, Prompts.ExtractorSystem(protocol)),
            new ChatMessage(ChatRole.User, Prompts.ExtractorUser(maskedTranscript, previousErrorCodes, ExtractorOutput.SchemaText, DataBlock.NewNonce())),
        };
        var response = await client.GetResponseAsync(messages, MeteredChatClient.Options(Role.Extractor, 1500, 0f), ct).ConfigureAwait(false);
        return response.Text;
    }
}
