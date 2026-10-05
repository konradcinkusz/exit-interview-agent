using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace ExitInterviewAgent.InterviewService.Submissions;

/// <summary>
/// Random bearer secrets (SECURITY-REVIEW section 5: a GUID is not a secret). A secret is bytes from the operating-system
/// CSPRNG, URL-safe base64; the server keeps only the SHA-256 of what it handed out. SHA-256 (not a slow hash) is
/// right here because the input is 256 bits of entropy, so there is nothing to brute-force.
/// </summary>
public static class SecretTokens
{
    public const int TicketBytes = 32;
    public const int TicketChars = 43;

    public static string NewTicket() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TicketBytes));

    public static bool IsWellFormedTicket(string? value) =>
        value is { Length: TicketChars } && value.All(IsBase64Url) && DecodesTo(value, TicketBytes);

    /// <summary>Lowercase hex of SHA-256 over the ASCII of the secret as presented.</summary>
    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(secret)));

    /// <summary>Constant-time equality of two hex digests of equal length.</summary>
    public static bool HashesEqual(string a, string b) =>
        a.Length == b.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    internal static bool IsBase64Url(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';

    internal static bool DecodesTo(string value, int bytes)
    {
        try { return WebEncoders.Base64UrlDecode(value).Length == bytes; }
        catch (FormatException) { return false; }
    }
}

/// <summary>
/// Receipt codes: 256 random bits plus a 16-bit checksum, URL-safe base64 (46 characters). The checksum is computable
/// by anyone from the rest of the code, so it adds no secrecy and reveals nothing about any stored code; it exists so a
/// typo is reported as a typo (400) instead of as a silent "deleted" (ADR-0029).
/// </summary>
public static class ReceiptCodes
{
    private const int SecretBytes = 32;
    private const int ChecksumBytes = 2;
    public const int Length = 46;

    public static string NewCode()
    {
        var bytes = new byte[SecretBytes + ChecksumBytes];
        RandomNumberGenerator.Fill(bytes.AsSpan(0, SecretBytes));
        Checksum(bytes.AsSpan(0, SecretBytes)).CopyTo(bytes.AsSpan(SecretBytes));
        return WebEncoders.Base64UrlEncode(bytes);
    }

    public static bool IsWellFormed(string? code)
    {
        if (code is not { Length: Length } || !code.All(SecretTokens.IsBase64Url))
        {
            return false;
        }
        byte[] bytes;
        try { bytes = WebEncoders.Base64UrlDecode(code); }
        catch (FormatException) { return false; }
        return bytes.Length == SecretBytes + ChecksumBytes
            && bytes.AsSpan(SecretBytes).SequenceEqual(Checksum(bytes.AsSpan(0, SecretBytes)));
    }

    private static byte[] Checksum(ReadOnlySpan<byte> secret) => SHA256.HashData(secret)[..ChecksumBytes];
}
