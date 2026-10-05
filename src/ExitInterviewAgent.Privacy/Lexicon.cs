using System.Collections.Frozen;

namespace ExitInterviewAgent.Privacy;

/// <summary>Static word lists for the name heuristics. Lower-case, compared with the invariant culture.</summary>
internal static class Lexicon
{
    private static readonly string[] EnglishGiven =
    [
        "james", "john", "robert", "michael", "william", "david", "richard", "joseph", "thomas", "charles", "christopher", "daniel",
        "matthew", "anthony", "andrew", "joshua", "kevin", "brian", "george", "edward", "ronald", "timothy", "jason", "jeffrey",
        "ryan", "jacob", "gary", "nicholas", "eric", "jonathan", "stephen", "larry", "justin", "scott", "brandon", "benjamin",
        "samuel", "gregory", "alexander", "patrick", "raymond", "dennis", "jerry", "tyler", "aaron", "henry", "douglas", "peter",
        "adam", "nathan", "zachary", "walter", "kyle", "harold", "carl", "arthur", "gerald", "roger", "keith", "jeremy", "terry",
        "lawrence", "sean", "christian", "albert", "joe", "ethan", "austin", "jesse", "ralph", "bruce", "oliver", "harry", "simon",
        "mary", "patricia", "jennifer", "linda", "elizabeth", "barbara", "susan", "jessica", "sarah", "karen", "lisa", "nancy",
        "betty", "margaret", "sandra", "ashley", "kimberly", "emily", "donna", "michelle", "carol", "amanda", "dorothy", "melissa",
        "deborah", "stephanie", "rebecca", "sharon", "laura", "cynthia", "kathleen", "amy", "angela", "shirley", "anna", "brenda",
        "pamela", "emma", "nicole", "helen", "samantha", "katherine", "christine", "debra", "rachel", "carolyn", "janet", "catherine",
        "maria", "heather", "diane", "olivia", "julie", "joyce", "victoria", "kelly", "christina", "lauren", "megan", "hannah",
        "andrea", "jacqueline", "teresa", "sophia", "natalie", "charlotte", "alice", "lucy", "claire", "sophie", "rebekah", "tom",
        "tim", "jim", "steve", "mike", "dave", "chris", "nick", "dan", "matt", "sam", "alex", "ben", "luke", "paul", "mark", "will",
    ];

    // Polish masculine -> declined below; feminine ending in -a -> declined below.
    private static readonly string[] PolishGiven =
    [
        "jan", "andrzej", "piotr", "krzysztof", "stanisław", "tomasz", "paweł", "józef", "marcin", "marek", "michał", "grzegorz",
        "jerzy", "tadeusz", "adam", "łukasz", "zbigniew", "ryszard", "dariusz", "henryk", "mariusz", "kazimierz", "wojciech",
        "robert", "mateusz", "marian", "rafał", "jacek", "janusz", "mirosław", "maciej", "sławomir", "jarosław", "kamil", "wiesław",
        "roman", "władysław", "jakub", "artur", "zdzisław", "edward", "mieczysław", "damian", "dawid", "przemysław", "sebastian",
        "czesław", "leszek", "daniel", "waldemar", "bartosz", "bartłomiej", "szymon", "filip", "karol", "patryk", "konrad",
        "anna", "maria", "katarzyna", "małgorzata", "agnieszka", "krystyna", "barbara", "ewa", "elżbieta", "zofia", "janina",
        "teresa", "joanna", "magdalena", "monika", "jadwiga", "danuta", "irena", "halina", "helena", "beata", "aleksandra",
        "marta", "dorota", "marianna", "grażyna", "jolanta", "stanisława", "iwona", "karolina", "bożena", "urszula", "justyna",
        "renata", "alicja", "paulina", "sylwia", "natalia", "wanda", "agata", "aneta", "izabela", "ewelina", "marzena", "wioletta",
        "anita", "kinga", "olga", "weronika", "klaudia", "dominika", "patrycja", "kamila", "julia", "zuzanna", "lena", "oliwia",
        "kasia", "basia", "gosia", "asia", "ola", "tomek", "marek", "darek", "staszek", "wojtek", "krzysiek", "michał", "kuba",
    ];

    /// <summary>Given names that are also ordinary words (or months): masked at sentence start only when fail-closed.</summary>
    public static readonly FrozenSet<string> AmbiguousGiven = new[]
    {
        "will", "mark", "rose", "grace", "bill", "art", "may", "april", "june", "august", "dawn", "hope", "joy", "faith", "frank",
        "max", "pat", "ray", "sue", "jack", "jean", "lee", "don", "dean", "chase", "sandy", "maja", "róża", "lilia", "iga",
        "viola", "violet", "amber", "ruby", "pearl", "holly",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static readonly FrozenSet<string> GivenNames = BuildGiven();

    private static FrozenSet<string> BuildGiven()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in EnglishGiven)
        {
            set.Add(n);
            set.Add(n + "'s");
            set.Add(n + "’s");
        }

        foreach (var n in PolishGiven)
            foreach (var form in PolishForms(n)) set.Add(form);
        return set.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>The nominative plus the common case endings (genitive, dative, accusative, instrumental, locative, vocative).</summary>
    private static IEnumerable<string> PolishForms(string n)
    {
        yield return n;
        if (n.EndsWith("eł", StringComparison.Ordinal))
        {
            var stem = n[..^2] + "ł";
            foreach (var e in new[] { "a", "owi", "em", "u" }) yield return stem + e;
        }
        else if (n.EndsWith("ek", StringComparison.Ordinal))
        {
            var stem = n[..^2] + "k";
            foreach (var e in new[] { "a", "owi", "iem", "u" }) yield return stem + e;
        }
        else if (n.EndsWith('a'))
        {
            var stem = n[..^1];
            foreach (var e in new[] { "y", "ie", "ę", "ą", "o", "i" }) yield return stem + e;
            if (stem.EndsWith('k') || stem.EndsWith('g')) yield return stem + "ę";
        }
        else
        {
            foreach (var e in new[] { "a", "owi", "em", "u", "iem", "ie", "ze" }) yield return n + e;
        }
    }

    public static readonly FrozenSet<string> Stop = new[]
    {
        // pronoun and contractions of "I"
        "i", "i'm", "i've", "i'd", "i'll", "i’m", "i’ve", "i’d", "i’ll",
        // speaker labels
        "user", "interviewer", "agent", "assistant", "respondent", "candidate", "interviewee", "employee", "employer", "participant",
        // titles (the cue is kept in the text; only the name after it is masked) and sentence openers that precede names
        "pan", "pani", "państwo", "mr", "mrs", "ms", "miss", "mx", "dr", "prof", "professor", "sir", "madam", "mgr", "dear", "ask", "call",
        "contact", "tell", "email", "write", "meet", "talk", "see", "ping", "mail", "message", "speak", "blame", "credit", "regards",
        "zapytaj", "skontaktuj", "zadzwoń", "napisz", "porozmawiaj", "pozdrawiam", "szanowny", "szanowna", "drogi", "droga",
        // calendar
        "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday", "january", "february", "march", "april", "may",
        "june", "july", "august", "september", "october", "november", "december", "christmas", "easter", "covid",
        // languages, nationalities, places
        "english", "polish", "german", "french", "spanish", "italian", "ukrainian", "russian", "american", "british", "european",
        "indian", "chinese", "dutch", "swedish", "poland", "polska", "polski", "polskie", "polskiej", "germany", "france", "spain",
        "italy", "ukraine", "russia", "europe", "america", "asia", "africa", "england", "london", "warsaw", "warszawa", "kraków",
        "krakow", "gdańsk", "gdansk", "wrocław", "wroclaw", "poznań", "poznan", "łódź", "lodz", "berlin", "paris", "usa", "uk", "eu",
        "warszawski", "warszawska", "krakowski", "krakowska", "gdański", "gdańska", "poznański", "wrocławski", "łódzki", "śląski",
        "europejski", "europejska", "amerykański", "angielski", "niemiecki", "francuski", "rosyjski", "ukraiński", "unia", "unii",
        // tools and technology
        "linkedin", "google", "microsoft", "slack", "jira", "teams", "zoom", "excel", "word", "office", "linux", "windows", "mac",
        "iphone", "android", "python", "java", "javascript", "typescript", "azure", "aws", "kubernetes", "docker", "github",
        "confluence", "notion", "trello", "salesforce", "sap", "oracle", "amazon", "facebook", "meta", "apple", "outlook", "gmail",
        "excel", "powerpoint", "sharepoint", "tableau", "figma", "git", "sql", "api", "ai", "chatgpt", "stackoverflow",
        // roles and organisational words
        "manager", "director", "engineer", "developer", "analyst", "designer", "consultant", "specialist", "lead", "senior", "junior",
        "head", "chief", "officer", "president", "vice", "executive", "associate", "intern", "product", "project", "program",
        "programme", "human", "resources", "team", "department", "division", "company", "corp", "inc", "ltd", "llc", "sp", "zoo",
        "group", "office", "hr", "management", "sales", "marketing", "engineering", "finance", "operations", "support", "legal",
        "ceo", "cto", "cfo", "coo", "cio", "vp", "qa", "it", "pm", "po", "owner", "master", "scrum", "agile", "kanban",
        // common capitalised sentence-style words in transcripts
        "yes", "no", "ok", "okay", "thanks", "thank", "please", "hello", "hi", "well", "so", "but", "and", "also", "however",
        "overall", "honestly", "basically", "actually", "maybe", "sure", "right", "good", "great", "bad", "first", "second", "third",
        "last", "next", "after", "before", "during", "since", "while", "when", "then", "there", "here", "this", "that", "these",
        "those", "what", "why", "how", "who", "which", "where", "because", "if", "in", "on", "at", "to", "for", "with", "from",
        "my", "our", "your", "their", "his", "her", "its", "we", "they", "he", "she", "it", "you", "me", "us", "them",
        "tak", "nie", "ale", "i", "oraz", "więc", "czyli", "ponieważ", "bo", "gdy", "kiedy", "jeśli", "jeżeli", "dlaczego", "jak",
        "co", "kto", "gdzie", "który", "która", "które", "ten", "ta", "to", "te", "mój", "moja", "moje", "nasz", "nasza", "nasze",
        "dziękuję", "proszę", "dzień", "dobry", "witam", "cześć", "szef", "szefowa", "firma", "firmie", "praca", "pracy",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Particles that may sit inside a multi-token name ("Jan van der Berg").</summary>
    public static readonly FrozenSet<string> NameParticles =
        new[] { "von", "van", "der", "den", "de", "di", "da", "del", "zu", "le", "la", "bin", "al", "el" }.ToFrozenSet(StringComparer.Ordinal);

    public static readonly FrozenSet<string> CurrencyCodes =
        new[] { "PLN", "USD", "EUR", "GBP", "CHF", "CZK", "SEK", "NOK", "DKK", "UAH", "JPY", "CAD", "AUD", "ZŁ" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Host-like strings that are technology names, not web addresses.</summary>
    public static readonly FrozenSet<string> NotHosts =
        new[] { "asp.net", "vb.net", "node.js", "next.js", "vue.js", "react.js", "express.js", "nuxt.js", "d3.js", "three.js", "ado.net" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}
