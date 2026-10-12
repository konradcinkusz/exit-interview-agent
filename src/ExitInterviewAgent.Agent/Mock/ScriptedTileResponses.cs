using System.Text.Json;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Mock;

/// <summary>
/// The tile-writer role of <see cref="ScriptedChatClient"/>: a deterministic, schema-valid answer built only from the
/// COVERED topics, their ratings and the language in the record data block. Quotes are never copied. Same caveat as the
/// rest of the mock: a seam for tests, not a measure of how a real model writes. Each family draft is padded with fixed,
/// neutral sentences up to its section minimum, so that the guard keeps it (ADR-0083).
/// </summary>
internal static class ScriptedTileResponses
{
    public static string Write(string user)
    {
        var language = "en";
        var covered = new List<(Topic Topic, int? Rating)>();
        var inside = false;
        foreach (var raw in user.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith(TilePrompts.RecordBegin, StringComparison.Ordinal)) { inside = true; continue; }
            if (line.StartsWith(TilePrompts.RecordEnd, StringComparison.Ordinal)) break;
            if (!inside || !line.StartsWith('{')) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("language", out var lang))
            {
                language = TileWording.Language(lang.GetString());
                continue;
            }
            if (!Wire.TryParse<Topic>(root.GetProperty("topic").GetString(), out var topic)) continue;
            var rating = root.GetProperty("rating");
            covered.Add((topic, rating.ValueKind == JsonValueKind.Number ? rating.GetInt32() : null));
        }

        var tiles = new List<object>();
        if (covered.Count > 0)
        {
            var all = covered.Select(c => Wire.Name(c.Topic)).ToArray();
            tiles.Add(Entry(language, "overview", new[] { "Overview", "Przegląd" }, string.Join(' ', covered.Select(c => TileWording.Summary(language, c.Topic, c.Rating))), all));

            var worked = covered.Where(c => c.Rating >= 4).ToList();
            if (worked.Count > 0)
                tiles.Add(Entry(language, "what_worked", new[] { "What worked", "Co się sprawdziło" }, string.Join(' ', worked.Select(c => TileWording.Summary(language, c.Topic, c.Rating))), Names(worked)));

            var improve = covered.Where(c => c.Rating <= 2).ToList();
            if (improve.Count > 0)
            {
                tiles.Add(Entry(language, "what_could_improve", new[] { "What could improve", "Co można poprawić" }, string.Join(' ', improve.Select(c => TileWording.Summary(language, c.Topic, c.Rating))), Names(improve)));
                tiles.Add(Entry(language, "for_the_next_person", new[] { "For the next person", "Dla następnej osoby" }, Ask(language, improve.Select(c => c.Topic)), Names(improve)));
            }

            // The four families (ADR-0083, spec §3.3). Only covered topics, ratings in words, no experience terms, the company token.
            tiles.Add(StructuredReview(language, covered, all));
            tiles.Add(Narrative(language, covered, all));
            tiles.Add(ProfessionalPost(language, covered, all));
            tiles.Add(ShortPost(language, covered, all));
        }

        return JsonSerializer.Serialize(new { tiles });
    }

    private static object Entry(string lang, string kind, string[] titles, string text, string[] basedOn) =>
        new { kind, title = lang == "pl" ? titles[1] : titles[0], text, basedOn };

    /// <summary>A family draft: its kind, its covered topics and its sections in the writer's order.</summary>
    private static object Family(string kind, string[] basedOn, params (string Key, string Text)[] sections) =>
        new { kind, basedOn, sections = sections.Select(s => new { key = s.Key, text = s.Text }).ToArray() };

    private static string[] Names(IEnumerable<(Topic Topic, int? Rating)> items) => items.Select(i => Wire.Name(i.Topic)).ToArray();

    /// <summary>Title, pros, cons and advice to management. Each section is padded to its minimum.</summary>
    private static object StructuredReview(string lang, List<(Topic Topic, int? Rating)> covered, string[] basedOn)
    {
        var pros = Summaries(lang, covered.Where(c => c.Rating >= 4));
        var cons = Summaries(lang, covered.Where(c => c.Rating <= 2));
        var negative = covered.Where(c => c.Rating <= 2).Select(c => TileWording.Label(lang, c.Topic)).ToList();
        var advice = negative.Count > 0
            ? (lang == "pl" ? "Warto przejrzeć: " : "Worth reviewing: ") + string.Join(", ", negative) + "."
            : (lang == "pl" ? "Warto zachować to, co działa." : "Worth keeping what works.");
        return Family("structured_review", basedOn,
            ("title", lang == "pl" ? "Notatki z rozmowy odchodzącej" : "Notes from my exit interview"),
            ("pros", Pad(pros, 40, Neutral(lang))),
            ("cons", Pad(cons, 40, Neutral(lang))),
            ("advice", Pad(advice, 40, Neutral(lang))));
    }

    /// <summary>A first-person narrative. The company is the token, never a name.</summary>
    private static object Narrative(string lang, List<(Topic Topic, int? Rating)> covered, string[] basedOn)
    {
        var lead = lang == "pl"
            ? "Chcę opisać mój czas w [FIRMA] własnymi słowami."
            : "I want to describe my time at [COMPANY] in my own words.";
        return Family("narrative", basedOn,
            ("title", lang == "pl" ? "Mój czas w [FIRMA]" : "My time at [COMPANY]"),
            ("body", Pad($"{lead} {Summaries(lang, covered)}", 600, Reflective(lang))));
    }

    /// <summary>A measured professional note: the covered topics, ratings in words.</summary>
    private static object ProfessionalPost(string lang, List<(Topic Topic, int? Rating)> covered, string[] basedOn)
    {
        var lead = lang == "pl"
            ? "Krótka, rzeczowa notatka o mojej rozmowie odchodzącej."
            : "A short, measured note on my exit interview.";
        return Family("professional_post", basedOn, ("body", Pad($"{lead} {Summaries(lang, covered)}", 300, Reflective(lang))));
    }

    /// <summary>A short post: the covered topics by name only, so it fits the 270-character limit.</summary>
    private static object ShortPost(string lang, List<(Topic Topic, int? Rating)> covered, string[] basedOn)
    {
        var lead = lang == "pl" ? "Mój wpis o tematach:" : "My note on the topics:";
        var labels = string.Join(", ", covered.Select(c => TileWording.Label(lang, c.Topic)));
        return Family("short_post", basedOn, ("body", Pad($"{lead} {labels}.", 80, Neutral(lang))));
    }

    /// <summary>Appends fixed sentences, in turn, until the text reaches <paramref name="min"/> characters.</summary>
    private static string Pad(string text, int min, string[] sentences)
    {
        var result = text;
        for (var i = 0; result.Length < min; i++) result = $"{result} {sentences[i % sentences.Length]}";
        return result;
    }

    /// <summary>Short neutral sentences that name no topic and no person.</summary>
    private static string[] Neutral(string lang) => lang == "pl"
        ? new[] { "To moja własna opinia, oparta na moich odpowiedziach.", "Nic tutaj nie wykracza poza to, co powiedziałem w rozmowie.", "Napisane z moich własnych odpowiedzi, bez nazwisk i danych o innych osobach." }
        : new[] { "This is my own view, based on my answers.", "Nothing here goes beyond what I said in the interview.", "Written from my own answers, without names or figures about others." };

    /// <summary>Longer reflective sentences for the narrative and the professional note. No topic name, no experience term.</summary>
    private static string[] Reflective(string lang) => lang == "pl"
        ? new[] { "Patrząc wstecz, to są punkty, o których chciałbym wiedzieć przed dołączeniem.", "Do dziś myślę o tym, jak wyglądały pierwsze miesiące i o co zapytałbym następnym razem.", "Piszę to jako własną relację i nie mówię w imieniu nikogo innego.", "Inne osoby mogły przeżyć te same miesiące inaczej." }
        : new[] { "Looking back, these are the points I would want to know before joining.", "I still think about how the first months went and what I would ask next time.", "I write this as my own account, and I do not speak for anyone else.", "Other people may have had a different experience of the same months." };

    private static string Summaries(string lang, IEnumerable<(Topic Topic, int? Rating)> items)
    {
        var text = string.Join(' ', items.Select(c => TileWording.Summary(lang, c.Topic, c.Rating)));
        return text.Length > 0 ? text : (lang == "pl" ? "brak." : "none.");
    }

    private static string Ask(string lang, IEnumerable<Topic> topics)
    {
        var labels = string.Join(", ", topics.Select(t => TileWording.Label(lang, t)));
        return lang == "pl" ? $"Warto zapytać przed podjęciem pracy o: {labels}." : $"Worth asking about before joining: {labels}.";
    }
}
