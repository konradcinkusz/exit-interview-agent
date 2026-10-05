using System.Text;
using System.Text.Json;

namespace ExitInterviewAgent.Providers;

/// <summary>The plain-language notice shown before the first interview with a provider. <see cref="RequiresConfirmation"/> is true when the transcript leaves this machine.</summary>
public sealed record DisclosureNotice(int Version, string Text, bool RequiresConfirmation);

public static class Disclosure
{
    /// <summary>Bump when the wording changes in substance: a remembered confirmation of an older version is asked again.</summary>
    public const int NoticeVersion = 1;

    public static DisclosureNotice For(ProviderSettings settings)
    {
        var sb = new StringBuilder();
        if (settings.Kind == ProviderKind.Mock)
        {
            sb.AppendLine("Scripted mock model: nothing is sent anywhere. It is a test seam that understands nothing, not an interviewer.");
            return new DisclosureNotice(NoticeVersion, sb.ToString(), false);
        }

        if (settings.IsLocal)
        {
            sb.AppendLine($"Local model: this interview is run by an AI interviewer. Your answers are processed by the model server at {settings.Endpoint}, on this computer.");
            sb.AppendLine("Through this program nothing leaves the machine. The transcript stays in memory and is not saved unless you pass --save-transcript. Ctrl-C or Ctrl-D stops the interview and discards everything.");
            if (settings.Kind == ProviderKind.OpenAiCompatible)
                sb.AppendLine("A gateway on this machine may itself forward requests to a remote provider; this program cannot see that. Check its configuration.");
            sb.AppendLine("The licence of the model weights you downloaded governs how you may use them; none are bundled here.");
            return new DisclosureNotice(NoticeVersion, sb.ToString(), false);
        }

        var credential = settings.ApiKey is null ? "without a key" : "using your own API key";
        sb.AppendLine("Before we start: where your answers go");
        sb.AppendLine();
        sb.AppendLine("This interview is run by an AI interviewer. To work, the whole conversation (every question and every answer you type) is sent to:");
        sb.AppendLine();
        sb.AppendLine($"    {settings.Info.DisplayName}, at {settings.Endpoint}, {credential}");
        sb.AppendLine();
        sb.AppendLine("That provider's terms, data retention and training rules apply to it. This project does not control them and has not verified them for your account: read them before you continue.");
        sb.AppendLine();
        sb.AppendLine("  - It is not sent to this project or to any server of ours. It stays in memory on this computer and is not saved unless you pass --save-transcript.");
        sb.AppendLine("  - If you finish, only a structured record (ratings and short quotes from your own words, personal names masked) is produced. This version does not send it anywhere.");
        sb.AppendLine("  - Ctrl-C or Ctrl-D at any time stops the interview and discards everything.");
        sb.AppendLine("  - To keep everything on your machine, use a local model: --provider ollama.");
        sb.AppendLine();
        sb.AppendLine($"More: {ProviderCatalog.ReadmeAnchor}");
        return new DisclosureNotice(NoticeVersion, sb.ToString(), true);
    }
}

/// <summary>
/// The only thing persisted about a confirmation, and only if the user opts in: a small local file listing which provider
/// endpoints (provider id and host, no key, no time, no model, no content) the user has confirmed at which notice version.
/// Deleting the file, or <c>exit-interview providers forget-confirmations</c>, forgets it. An unreadable file counts as empty.
/// </summary>
public sealed class ConfirmationStore(string path)
{
    public string Path { get; } = path;

    private sealed record Entry(string Provider, string Endpoint, int NoticeVersion);

    private sealed record Document(List<Entry> Confirmed);

    public bool IsConfirmed(ProviderSettings settings) =>
        Read().Confirmed.Any(e => e.Provider == settings.Info.Id && e.Endpoint == settings.Endpoint && e.NoticeVersion >= Disclosure.NoticeVersion);

    public int Count => Read().Confirmed.Count;

    public void Remember(ProviderSettings settings)
    {
        var doc = Read();
        doc.Confirmed.RemoveAll(e => e.Provider == settings.Info.Id && e.Endpoint == settings.Endpoint);
        doc.Confirmed.Add(new Entry(settings.Info.Id, settings.Endpoint, Disclosure.NoticeVersion));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        File.WriteAllText(Path, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>Deletes the file. True when there was one.</summary>
    public bool Forget()
    {
        if (!File.Exists(Path)) return false;
        File.Delete(Path);
        return true;
    }

    private Document Read()
    {
        try
        {
            if (!File.Exists(Path) || new FileInfo(Path).Length > 64 * 1024) return new Document([]);
            return JsonSerializer.Deserialize<Document>(File.ReadAllText(Path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) is { Confirmed: not null } d ? d : new Document([]);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new Document([]);
        }
    }
}
