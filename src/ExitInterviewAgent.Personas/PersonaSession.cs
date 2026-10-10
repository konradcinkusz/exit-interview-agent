using System.Security.Cryptography;
using System.Text;
using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Records;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.Personas;

/// <summary>
/// One complete simulated interview: a persona on one side, the agent on the other, a model behind the agent. By default
/// the model is the offline <see cref="ScriptedChatClient"/>; the eval harness passes a real <see cref="IChatClient"/>.
/// Everything else (clock, interview id, persona choices) is seed-derived, so a run with the mock model is reproducible byte for byte.
/// </summary>
public static class PersonaSession
{
    public static async Task<InterviewResult> RunAsync(
        PersonaDefinition persona, int seed, IChatClient? model = null, Func<string, string>? decorate = null, ILogger? logger = null, CancellationToken ct = default)
    {
        var clock = new SimulatedClock();
        var options = new InterviewOptions(persona.Employer.Ref, persona.Context.ToRecordContext())
        {
            // The persona's language selects the protocol wording (Y2); English personas keep the English protocol.
            Protocol = InterviewProtocol.For(persona.Language),
            EmployerNames = persona.Employer.Names.ToArray(),
            IdFactory = () => DemoInterviewId(persona.Id, seed),
            Clock = clock,
            Logger = logger,
        };
        var runner = InterviewRunner.Create(model ?? new ScriptedChatClient(), options);
        return await runner.RunAsync(new PersonaInterviewee(persona, seed, clock, decorate), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A reproducible interview id for demos and tests: the first 128 bits of SHA-256 over the persona id and seed.
    /// Real interviews use <see cref="InterviewId.NewRandom"/>; an id derived from anything is never used outside simulation.
    /// </summary>
    public static InterviewId DemoInterviewId(string personaId, int seed) =>
        InterviewId.Parse(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"exit-interview-agent/simulation/{personaId}/{seed}"))[..16]));
}
