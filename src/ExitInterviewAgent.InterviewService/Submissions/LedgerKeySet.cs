using System.Security.Cryptography;
using System.Text;

namespace ExitInterviewAgent.InterviewService.Submissions;

/// <summary>
/// The rotatable HMAC key set behind the submission ledger (ADR-0028). New entries are tagged with the active key;
/// a lookup computes the tag under EVERY key still listed, so rotating does not reset duplicate suppression until an
/// old key is removed from configuration, which the operator does after the ledger window has passed.
/// The key bytes live only here: never logged, never persisted, never in a default.
/// </summary>
public sealed class LedgerKeySet
{
    public const int MinSecretBytes = 32;
    private const string Domain = "exit-interview-agent/ledger/v1";

    private readonly Dictionary<string, byte[]> _keys;

    private LedgerKeySet(string activeId, Dictionary<string, byte[]> keys, bool ephemeral)
    {
        ActiveKeyId = activeId;
        _keys = keys;
        IsEphemeral = ephemeral;
    }

    public string ActiveKeyId { get; }

    /// <summary>True when the key was generated for this process (Development without configuration): entries do not survive a restart.</summary>
    public bool IsEphemeral { get; }

    public IReadOnlyCollection<string> KeyIds => _keys.Keys;

    /// <summary>
    /// Reads the key set. Outside Development a missing, malformed or too-short key is a startup failure; in Development an
    /// absent configuration yields an ephemeral key (the caller logs that it did so, never the key).
    /// </summary>
    public static LedgerKeySet Create(LedgerOptions options, bool isDevelopment)
    {
        var configured = options.Keys.Where(k => !string.IsNullOrWhiteSpace(k.Id) || !string.IsNullOrWhiteSpace(k.Secret)).ToList();
        if (configured.Count == 0)
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException("Ledger:Keys is not configured. The service does not start without a ledger key outside Development.");
            }
            var id = "dev-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
            return new LedgerKeySet(id, new() { [id] = RandomNumberGenerator.GetBytes(MinSecretBytes) }, ephemeral: true);
        }

        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var key in configured)
        {
            if (string.IsNullOrWhiteSpace(key.Id) || key.Id.Length > 32 || !key.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            {
                throw new InvalidOperationException("Every ledger key needs an Id of 1-32 characters from [A-Za-z0-9_-].");
            }
            if (!TryDecode(key.Secret, out var secret) || secret.Length < MinSecretBytes)
            {
                throw new InvalidOperationException($"Ledger key '{key.Id}' needs a Secret of at least {MinSecretBytes} bytes, base64 encoded.");
            }
            if (!keys.TryAdd(key.Id, secret))
            {
                throw new InvalidOperationException($"Ledger key id '{key.Id}' is listed twice.");
            }
        }
        var active = options.ActiveKeyId;
        if (string.IsNullOrWhiteSpace(active) || !keys.ContainsKey(active))
        {
            throw new InvalidOperationException("Ledger:ActiveKeyId must name one of the configured ledger keys.");
        }
        return new LedgerKeySet(active, keys, ephemeral: false);
    }

    /// <summary>Tag of (sub, employerRef) under one key: lowercase hex HMAC-SHA-256 over a length-prefixed encoding (no ambiguous concatenation).</summary>
    public string Tag(string keyId, string sub, string employerRef)
    {
        var s = Encoding.UTF8.GetBytes(sub);
        var e = Encoding.UTF8.GetBytes(employerRef);
        var d = Encoding.ASCII.GetBytes(Domain);
        var message = new byte[d.Length + 8 + s.Length + e.Length];
        var span = message.AsSpan();
        d.CopyTo(span);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(span[d.Length..], s.Length);
        s.CopyTo(span[(d.Length + 4)..]);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(span[(d.Length + 4 + s.Length)..], e.Length);
        e.CopyTo(span[(d.Length + 8 + s.Length)..]);
        return Convert.ToHexStringLower(HMACSHA256.HashData(_keys[keyId], message));
    }

    public string ActiveTag(string sub, string employerRef) => Tag(ActiveKeyId, sub, employerRef);

    /// <summary>The tag under every key still in the set, for the duplicate lookup.</summary>
    public IReadOnlyList<string> AllTags(string sub, string employerRef) => [.. _keys.Keys.Select(id => Tag(id, sub, employerRef))];

    private static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        try { bytes = Convert.FromBase64String(value.Trim()); return true; }
        catch (FormatException) { return false; }
    }
}
