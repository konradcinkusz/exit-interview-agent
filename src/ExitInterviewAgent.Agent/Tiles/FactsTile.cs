using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tiles;

/// <summary>
/// The Facts tile, built by CODE from the record: rating and confidence per covered topic, "no data" for the others. No
/// model text and no quote goes into it. It still passes through <see cref="ITileGuard"/> like every other candidate.
/// </summary>
public static class FactsTile
{
    public static CandidateTile Build(InterviewRecord record)
    {
        var lang = TileWording.Language(record.Interview.Language);
        var lines = new List<string>();
        var basedOn = new List<string>();
        foreach (var (topic, entry) in record.Topics.Enumerate())
        {
            if (entry.Status == TopicStatus.NoData)
            {
                lines.Add(TileWording.NoData(lang, topic));
                continue;
            }
            basedOn.Add(Wire.Name(topic));
            lines.Add(TileWording.FactLine(lang, topic, entry.Rating, entry.Confidence));
        }
        return new CandidateTile(TileKind.Facts, TileWording.FactsTitle(lang), string.Join('\n', lines), basedOn);
    }
}

/// <summary>
/// Fixed wording for the code-built and the scripted tiles, in the two languages a record can carry. Any other language
/// gets English. Rating words follow the number: 1-2 negative, 3 mixed, 4-5 positive.
/// </summary>
internal static class TileWording
{
    private static readonly string[] EnLabels = ["Onboarding", "Management", "Growth", "Pay vs promises", "Culture", "Reason for leaving"];
    private static readonly string[] PlLabels = ["Wdrożenie", "Zarządzanie", "Rozwój", "Wynagrodzenie a obietnice", "Kultura", "Powód odejścia"];

    public static string Language(string? language) => language == "pl" ? "pl" : "en";

    public static string FactsTitle(string lang) => lang == "pl" ? "Fakty z twoich ocen" : "Facts from your ratings";

    public static string NoData(string lang, Topic topic) =>
        lang == "pl" ? $"{Label(lang, topic)}: brak danych." : $"{Label(lang, topic)}: no data.";

    public static string FactLine(string lang, Topic topic, int? rating, Confidence? confidence)
    {
        var label = Label(lang, topic);
        var conf = ConfidenceWord(lang, confidence);
        if (lang == "pl")
            return rating is { } r ? $"{label}: ocena {r} z 5, pewność {conf}." : $"{label}: bez oceny, pewność {conf}.";
        return rating is { } e ? $"{label}: rating {e} of 5, confidence {conf}." : $"{label}: no rating, confidence {conf}.";
    }

    /// <summary>One summary sentence for a covered topic: the rating in words that match the number, and the number.</summary>
    public static string Summary(string lang, Topic topic, int? rating)
    {
        var label = Label(lang, topic);
        if (rating is not { } r) return lang == "pl" ? $"{label}: bez oceny." : $"{label}: no rating.";
        return lang == "pl" ? $"{label}: {RatingWord(lang, r)}, {r} z 5." : $"{label}: {RatingWord(lang, r)}, {r} of 5.";
    }

    public static string RatingWord(string lang, int rating) => (lang, rating) switch
    {
        ("pl", <= 2) => "negatywnie",
        ("pl", 3) => "mieszanie",
        ("pl", _) => "pozytywnie",
        (_, <= 2) => "negative",
        (_, 3) => "mixed",
        _ => "positive",
    };

    public static string Label(string lang, Topic topic) => (lang == "pl" ? PlLabels : EnLabels)[(int)topic];

    private static string ConfidenceWord(string lang, Confidence? confidence) => (lang, confidence) switch
    {
        ("pl", Confidence.Low) => "niska",
        ("pl", Confidence.Medium) => "średnia",
        ("pl", Confidence.High) => "wysoka",
        ("pl", _) => "nieznana",
        (_, { } c) => Wire.Name(c),
        _ => "unknown",
    };
}
