using System.Security.Cryptography;
using System.Text;

namespace ExitInterviewAgent.InterviewService.Billing;

/// <summary>
/// The <c>Stripe-Signature</c> scheme, implemented here without a provider SDK (ADR-0077): the header is <c>t=&lt;unix&gt;,v1=&lt;hex&gt;</c>
/// (several <c>v1</c> values are allowed during a secret rotation), and each <c>v1</c> is HMAC-SHA256 over the ASCII bytes
/// <c>t</c>, a dot and the raw body. The comparison is constant time over the decoded bytes, and a timestamp outside the tolerance
/// (in either direction) is refused, so a captured request cannot be replayed later.
/// </summary>
public static class StripeWebhookSignature
{
    /// <summary>Verifies the header for the raw body. Any malformed part is a failed check, never an exception.</summary>
    public static bool Verify(ReadOnlySpan<byte> body, string? header, string secret, DateTimeOffset now, TimeSpan tolerance)
    {
        if (string.IsNullOrEmpty(header) || string.IsNullOrEmpty(secret)) return false;
        if (!TryParse(header, out var timestamp, out var signatures)) return false;
        if (Math.Abs((now - DateTimeOffset.FromUnixTimeSeconds(timestamp)).TotalSeconds) > tolerance.TotalSeconds) return false;

        var expected = Mac(body, timestamp, secret);
        var matched = false;
        foreach (var candidate in signatures)
        {
            // Every candidate is compared, so the loop's length does not reveal which one matched.
            matched |= CryptographicOperations.FixedTimeEquals(candidate, expected);
        }
        return matched;
    }

    /// <summary>The header for a body at a given time. The provider does this; the service uses it only to sign test and fake events.</summary>
    public static string Header(ReadOnlySpan<byte> body, string secret, long unixSeconds)
    {
        var signature = Convert.ToHexString(Mac(body, unixSeconds, secret)).ToLowerInvariant();
        return $"t={unixSeconds},v1={signature}";
    }

    private static byte[] Mac(ReadOnlySpan<byte> body, long timestamp, string secret)
    {
        var prefix = Encoding.ASCII.GetBytes(timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        var payload = new byte[prefix.Length + body.Length];
        prefix.CopyTo(payload, 0);
        body.CopyTo(payload.AsSpan(prefix.Length));
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
    }

    private static bool TryParse(string header, out long timestamp, out List<byte[]> signatures)
    {
        timestamp = 0;
        signatures = [];
        var haveTimestamp = false;
        foreach (var part in header.Split(','))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) continue;
            switch (pair[0].Trim())
            {
                case "t" when long.TryParse(pair[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var t):
                    timestamp = t;
                    haveTimestamp = true;
                    break;
                case "v1":
                    try
                    {
                        signatures.Add(Convert.FromHexString(pair[1].Trim()));
                    }
                    catch (FormatException)
                    {
                        // A malformed candidate is simply not a match; the others still count.
                    }
                    break;
            }
        }
        return haveTimestamp && signatures.Count > 0;
    }
}
