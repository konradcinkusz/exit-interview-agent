namespace ExitInterviewAgent.Records;

/// <summary>Employer reference: opaque, strict pattern. The registry that issues it is out of scope.</summary>
public static class EmployerRef
{
    public static bool IsValid(string? value) =>
        value is { Length: >= 3 and <= 64 } && Pattern.IsMatch(value);

    private static readonly System.Text.RegularExpressions.Regex Pattern =
        new("^[a-z0-9]+(-[a-z0-9]+)*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}

public sealed record RecordContext(TenureBand TenureBand, SeniorityBand? SeniorityBand = null, FunctionBand? FunctionBand = null);

public sealed record InterviewMetadata(string ProtocolVersion, string Language, DurationBand DurationBand, TurnBand TurnBand);

/// <summary>One topic. Build with <see cref="NoData"/> or <see cref="Covered"/>; both enforce the schema's invariants.</summary>
public sealed record TopicEntry
{
    public const int MaxQuotes = 5;
    public const int MaxQuoteLength = 400;

    public TopicStatus Status { get; }
    public int? Rating { get; }
    public Confidence? Confidence { get; }
    public IReadOnlyList<string> Quotes { get; }

    private TopicEntry(TopicStatus status, int? rating, Confidence? confidence, IReadOnlyList<string> quotes)
    {
        Status = status;
        Rating = rating;
        Confidence = confidence;
        Quotes = quotes;
    }

    /// <summary>Not discussed, declined or not answerable. Distinct from a low rating.</summary>
    public static TopicEntry NoData { get; } = new(TopicStatus.NoData, null, null, []);

    public static TopicEntry Covered(int? rating, Confidence confidence, IEnumerable<string> quotes)
    {
        if (rating is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(rating), "Rating is 1-5 or null.");
        var list = quotes.ToArray();
        if (list.Length is 0 or > MaxQuotes) throw new ArgumentException($"A covered topic carries 1-{MaxQuotes} quotes.", nameof(quotes));
        if (list.Any(q => string.IsNullOrWhiteSpace(q) || q.Length > MaxQuoteLength))
            throw new ArgumentException($"Quotes are non-blank and at most {MaxQuoteLength} characters.", nameof(quotes));
        return new TopicEntry(TopicStatus.Covered, rating, confidence, Array.AsReadOnly(list));
    }

    public bool Equals(TopicEntry? other) =>
        other is not null && Status == other.Status && Rating == other.Rating && Confidence == other.Confidence
        && Quotes.SequenceEqual(other.Quotes, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Status, Rating, Confidence, Quotes.Count);
}

/// <summary>The six topics. Every topic is always present; absence of data is expressed by <see cref="TopicEntry.NoData"/>.</summary>
public sealed record TopicSet(
    TopicEntry Onboarding,
    TopicEntry Management,
    TopicEntry Growth,
    TopicEntry PayVsPromises,
    TopicEntry Culture,
    TopicEntry ReasonForLeaving)
{
    public TopicEntry this[Topic topic] => topic switch
    {
        Topic.Onboarding => Onboarding,
        Topic.Management => Management,
        Topic.Growth => Growth,
        Topic.PayVsPromises => PayVsPromises,
        Topic.Culture => Culture,
        Topic.ReasonForLeaving => ReasonForLeaving,
        _ => throw new ArgumentOutOfRangeException(nameof(topic)),
    };

    public IEnumerable<(Topic Topic, TopicEntry Entry)> Enumerate() =>
        Enum.GetValues<Topic>().Select(t => (t, this[t]));
}

/// <summary>
/// Exit interview record, schema v1. Immutable. It carries no per-person identifier of any kind: no user id,
/// account id, email, IP address, name or timestamp (ADR-0011; enforced by the architecture tests).
/// </summary>
public sealed record InterviewRecord(
    InterviewId InterviewId,
    string EmployerRef,
    RecordContext Context,
    TopicSet Topics,
    bool PiiMasked,
    InterviewMetadata Interview)
{
    public const string SchemaVersion = "1";
}
