using System.Text.Json;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Mock;

/// <summary>
/// The tile-writer role of <see cref="ScriptedChatClient"/>: a deterministic, schema-valid answer built only from the
/// COVERED topics, their ratings and the language in the record data block. Quotes are never copied. Same caveat as the
/// rest of the mock: a seam for tests, not a measure of how a real model writes.
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

            tiles.Add(Entry(language, "short_note", new[] { "Short note", "Krótka notatka" }, ShortNote(language, covered), all));
        }

        return JsonSerializer.Serialize(new { tiles });
    }

    private static object Entry(string lang, string kind, string[] titles, string text, string[] basedOn) =>
        new { kind, title = lang == "pl" ? titles[1] : titles[0], text, basedOn };

    private static string[] Names(IEnumerable<(Topic Topic, int? Rating)> items) => items.Select(i => Wire.Name(i.Topic)).ToArray();

    private static string Ask(string lang, IEnumerable<Topic> topics)
    {
        var labels = string.Join(", ", topics.Select(t => TileWording.Label(lang, t)));
        return lang == "pl" ? $"Warto zapytać przed podjęciem pracy o: {labels}." : $"Worth asking about before joining: {labels}.";
    }

    private static string ShortNote(string lang, List<(Topic Topic, int? Rating)> covered)
    {
        var negative = covered.Where(c => c.Rating <= 2).Select(c => TileWording.Label(lang, c.Topic));
        var positive = covered.Where(c => c.Rating >= 4).Select(c => TileWording.Label(lang, c.Topic));
        var n = covered.Count;
        return lang == "pl"
            ? $"Omówione tematy: {n}. Negatywnie: {Or(negative, lang)}. Pozytywnie: {Or(positive, lang)}."
            : $"Topics covered: {n}. Negative: {Or(negative, lang)}. Positive: {Or(positive, lang)}.";
    }

    private static string Or(IEnumerable<string> items, string lang)
    {
        var joined = string.Join(", ", items);
        return joined.Length > 0 ? joined : (lang == "pl" ? "brak" : "none");
    }
}
