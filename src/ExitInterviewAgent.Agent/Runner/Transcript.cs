using System.Text;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Runner;

/// <summary>One turn of the MASKED transcript. Interviewee text here has already been through the PII guard.</summary>
public sealed record Turn(int Index, Speaker Speaker, TurnKind Kind, Topic? Topic, string Text);

/// <summary>
/// The only transcript the agent keeps and the only one any model sees. Raw interviewee text is masked on arrival and
/// never stored; a withdrawn interview discards this object entirely.
/// </summary>
public sealed class Transcript
{
    private readonly List<Turn> _turns = [];

    public IReadOnlyList<Turn> Turns => _turns;

    public int IntervieweeTurns => _turns.Count(t => t.Speaker == Speaker.Interviewee);

    public void Add(Speaker speaker, TurnKind kind, Topic? topic, string text) =>
        _turns.Add(new Turn(_turns.Count + 1, speaker, kind, topic, text));

    /// <summary>The interviewee's words on one topic, turns joined by a line break.</summary>
    public string IntervieweeText(Topic topic) =>
        string.Join('\n', _turns.Where(t => t.Speaker == Speaker.Interviewee && t.Topic == topic).Select(t => t.Text));

    /// <summary>All of the interviewee's words (the fidelity haystack for quotes).</summary>
    public string IntervieweeText() =>
        string.Join('\n', _turns.Where(t => t.Speaker == Speaker.Interviewee).Select(t => t.Text));

    public string Render()
    {
        var sb = new StringBuilder();
        foreach (var t in _turns)
            sb.Append(t.Speaker == Speaker.Interviewer ? "Interviewer: " : "Interviewee: ").Append(t.Text).Append('\n');
        return sb.ToString();
    }
}
