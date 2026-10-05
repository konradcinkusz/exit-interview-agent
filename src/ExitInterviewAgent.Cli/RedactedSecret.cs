using System.Diagnostics;

namespace ExitInterviewAgent.Cli;

/// <summary>
/// A bearer secret (a submission ticket or a receipt code). It cannot be printed by accident: <see cref="ToString"/> and the debugger
/// view say "[redacted]", it is not serialisable, and there is no implicit conversion to string. The text is read back only by
/// <see cref="Reveal"/>, which exists for the one place that puts it in a request header. The characters are held in an array that
/// <see cref="Dispose"/> overwrites; a .NET string cannot be wiped, so the copy made for the header is short-lived but not erased.
/// </summary>
[DebuggerDisplay("[redacted]")]
internal sealed class RedactedSecret : IDisposable
{
    private readonly char[] _chars;

    private RedactedSecret(char[] chars) => _chars = chars;

    /// <summary>Header-safe shape only (URL-safe base64 alphabet, 16-256 characters). The real format is the server's business.</summary>
    public static bool TryCreate(string? text, out RedactedSecret? secret)
    {
        secret = null;
        var value = text?.Trim();
        if (value is not { Length: >= 16 and <= 256 } || !value.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_')) return false;
        secret = new RedactedSecret(value.ToCharArray());
        return true;
    }

    public string Reveal() => new(_chars);

    public int Length => _chars.Length;

    public void Dispose() => Array.Clear(_chars);

    public override string ToString() => "[redacted]";
}
