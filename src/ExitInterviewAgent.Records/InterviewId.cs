using System.Security.Cryptography;

namespace ExitInterviewAgent.Records;

/// <summary>
/// Pseudonymous interview id: 128 random bits, 32 lowercase hex characters. Generated from the operating
/// system CSPRNG and from nothing else, so it cannot be recomputed from, or linked to, any person or account.
/// </summary>
public readonly record struct InterviewId
{
    public const int HexLength = 32;

    private readonly string? _value;

    private InterviewId(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException("Default InterviewId is not a valid id.");

    public static InterviewId NewRandom() => new(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(HexLength / 2)));

    public static bool TryParse(string? text, out InterviewId id)
    {
        id = default;
        if (text is null || text.Length != HexLength) return false;
        foreach (var c in text)
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        id = new InterviewId(text);
        return true;
    }

    public static InterviewId Parse(string text) =>
        TryParse(text, out var id) ? id : throw new FormatException("Not a 32-character lowercase hex interview id.");

    public override string ToString() => Value;
}
