using System.Security.Cryptography;
using ExitInterviewAgent.InterviewService.Submissions;

namespace ExitInterviewAgent.InterviewService.Tests.Submissions;

public sealed class LedgerKeySetTests
{
    private static string Secret(byte fill = 7, int length = 32) => Convert.ToBase64String(Enumerable.Repeat(fill, length).ToArray());

    private static LedgerOptions Options(string active, params (string Id, string Secret)[] keys) =>
        new() { ActiveKeyId = active, Keys = [.. keys.Select(k => new LedgerKeyOptions { Id = k.Id, Secret = k.Secret })] };

    [Fact]
    public void Outside_development_the_service_refuses_to_start_without_a_key()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LedgerKeySet.Create(new LedgerOptions(), isDevelopment: false));
        Assert.Contains("Ledger:Keys", ex.Message);
    }

    [Fact]
    public void In_development_without_configuration_an_ephemeral_key_is_generated_and_flagged()
    {
        var a = LedgerKeySet.Create(new LedgerOptions(), isDevelopment: true);
        var b = LedgerKeySet.Create(new LedgerOptions(), isDevelopment: true);

        Assert.True(a.IsEphemeral);
        Assert.NotEqual(a.ActiveTag("s", "e"), b.ActiveTag("s", "e")); // a fresh random key each time
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!!")]
    public void A_missing_or_malformed_secret_is_refused_even_in_development(string? secret)
    {
        Assert.Throws<InvalidOperationException>(() => LedgerKeySet.Create(Options("k1", ("k1", secret!)), isDevelopment: true));
    }

    [Fact]
    public void A_short_secret_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LedgerKeySet.Create(Options("k1", ("k1", Secret(length: 31))), isDevelopment: false));
        Assert.DoesNotContain(Secret(length: 31), ex.Message);
    }

    [Fact]
    public void The_active_key_must_be_one_of_the_listed_keys_and_ids_must_be_unique_and_safe()
    {
        Assert.Throws<InvalidOperationException>(() => LedgerKeySet.Create(Options("missing", ("k1", Secret())), false));
        Assert.Throws<InvalidOperationException>(() => LedgerKeySet.Create(Options("k1", ("k1", Secret()), ("k1", Secret(8))), false));
        Assert.Throws<InvalidOperationException>(() => LedgerKeySet.Create(Options("k 1", ("k 1", Secret())), false));
    }

    [Fact]
    public void The_tag_depends_on_every_input_and_is_stable()
    {
        var keys = LedgerKeySet.Create(Options("k1", ("k1", Secret(1)), ("k2", Secret(2))), false);

        Assert.Equal(keys.ActiveTag("sub", "emp"), keys.ActiveTag("sub", "emp"));
        Assert.NotEqual(keys.ActiveTag("sub", "emp"), keys.ActiveTag("sub2", "emp"));
        Assert.NotEqual(keys.ActiveTag("sub", "emp"), keys.ActiveTag("sub", "emp2"));
        Assert.NotEqual(keys.Tag("k1", "sub", "emp"), keys.Tag("k2", "sub", "emp"));
        Assert.Equal(64, keys.ActiveTag("sub", "emp").Length);
    }

    [Fact]
    public void The_encoding_is_unambiguous_so_moving_a_character_between_the_fields_changes_the_tag()
    {
        var keys = LedgerKeySet.Create(Options("k1", ("k1", Secret())), false);

        Assert.NotEqual(keys.ActiveTag("a", "bc"), keys.ActiveTag("ab", "c"));
        Assert.NotEqual(keys.ActiveTag("a\0b", "c"), keys.ActiveTag("a", "b\0c"));
    }

    [Fact]
    public void Rotation_old_entries_are_still_found_under_the_old_key_and_new_entries_use_the_new_one()
    {
        var before = LedgerKeySet.Create(Options("k1", ("k1", Secret(1))), false);
        var oldTag = before.ActiveTag("sub", "emp");

        var after = LedgerKeySet.Create(Options("k2", ("k1", Secret(1)), ("k2", Secret(2))), false);

        Assert.Equal("k2", after.ActiveKeyId);
        Assert.Contains(oldTag, after.AllTags("sub", "emp"));      // lookup still matches the entry written before rotation
        Assert.NotEqual(oldTag, after.ActiveTag("sub", "emp"));    // but new entries carry the new key
        Assert.Equal(2, after.AllTags("sub", "emp").Count);
    }

    [Fact]
    public void A_retired_key_no_longer_matches_anything()
    {
        var before = LedgerKeySet.Create(Options("k1", ("k1", Secret(1))), false);
        var oldTag = before.ActiveTag("sub", "emp");

        var retired = LedgerKeySet.Create(Options("k2", ("k2", Secret(2))), false);

        Assert.DoesNotContain(oldTag, retired.AllTags("sub", "emp"));
    }

    [Fact]
    public void The_tag_is_the_documented_hmac()
    {
        var keys = LedgerKeySet.Create(Options("k1", ("k1", Secret(9))), false);
        byte[] message = [.. "exit-interview-agent/ledger/v1"u8, 0, 0, 0, 3, .. "sub"u8, 0, 0, 0, 3, .. "emp"u8];

        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Enumerable.Repeat((byte)9, 32).ToArray(), message));

        Assert.Equal(expected, keys.ActiveTag("sub", "emp"));
    }
}

public sealed class SecretTokenTests
{
    [Fact]
    public void Receipt_codes_are_46_characters_random_and_carry_a_checksum()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => ReceiptCodes.NewCode()).ToList();

        Assert.All(codes, c => Assert.True(ReceiptCodes.IsWellFormed(c)));
        Assert.Equal(200, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Equal(ReceiptCodes.Length, c.Length));
    }

    [Fact]
    public void A_receipt_code_carries_at_least_128_bits_of_randomness()
    {
        // 32 random bytes (256 bits) + 2 checksum bytes = 34 bytes = 46 base64url characters.
        Assert.Equal(34, Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(ReceiptCodes.NewCode()).Length);
    }

    [Fact]
    public void A_single_changed_character_fails_the_checksum()
    {
        var code = ReceiptCodes.NewCode();
        var typo = (code[0] == 'A' ? 'B' : 'A') + code[1..];

        Assert.False(ReceiptCodes.IsWellFormed(typo));
        Assert.False(ReceiptCodes.IsWellFormed(code[..^1]));
        Assert.False(ReceiptCodes.IsWellFormed(code + "A"));
        Assert.False(ReceiptCodes.IsWellFormed(null));
        Assert.False(ReceiptCodes.IsWellFormed(new string('A', ReceiptCodes.Length)));
        Assert.False(ReceiptCodes.IsWellFormed(code.Replace(code[5], '*')));
    }

    [Fact]
    public void Hashes_are_sha256_hex_and_compare_in_constant_time_semantics()
    {
        var hash = SecretTokens.Hash("abc");

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        Assert.True(SecretTokens.HashesEqual(hash, SecretTokens.Hash("abc")));
        Assert.False(SecretTokens.HashesEqual(hash, SecretTokens.Hash("abd")));
        Assert.False(SecretTokens.HashesEqual(hash, hash[..^1]));
    }

    [Fact]
    public void Tickets_are_43_characters_of_256_random_bits()
    {
        var tickets = Enumerable.Range(0, 200).Select(_ => SecretTokens.NewTicket()).ToList();

        Assert.All(tickets, t => Assert.True(SecretTokens.IsWellFormedTicket(t)));
        Assert.Equal(200, tickets.Distinct().Count());
        Assert.False(SecretTokens.IsWellFormedTicket("short"));
        Assert.False(SecretTokens.IsWellFormedTicket(null));
    }
}

public sealed class TimeBucketTests
{
    [Theory]
    [InlineData("2026-10-05T00:00:00Z", "2026-10-05")] // a Monday
    [InlineData("2026-10-11T23:59:59Z", "2026-10-05")] // Sunday of the same week
    [InlineData("2026-10-12T00:00:00Z", "2026-10-12")]
    [InlineData("2026-10-07T12:34:56+05:00", "2026-10-05")]
    public void A_week_bucket_is_the_monday_in_utc(string instant, string monday)
    {
        Assert.Equal(DateOnly.Parse(monday), TimeBuckets.WeekStart(DateTimeOffset.Parse(instant)));
        Assert.Equal(DateOnly.Parse(monday).AddDays(7), TimeBuckets.WeekEnd(TimeBuckets.WeekStart(DateTimeOffset.Parse(instant))));
    }

    [Fact]
    public void Ticket_expiry_is_rounded_up_to_the_granularity()
    {
        var step = TimeSpan.FromMinutes(5);

        Assert.Equal(new DateTimeOffset(2026, 10, 5, 12, 20, 0, TimeSpan.Zero), TicketStore.RoundUp(new DateTimeOffset(2026, 10, 5, 12, 15, 1, TimeSpan.Zero), step));
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 12, 15, 0, TimeSpan.Zero), TicketStore.RoundUp(new DateTimeOffset(2026, 10, 5, 12, 15, 0, TimeSpan.Zero), step));
    }
}
