using System.Reflection;
using System.Text.RegularExpressions;

namespace ExitInterviewAgent.Privacy.Tests;

public class PiiDetectorTests
{
    private static readonly PiiDetector Detector = new(new PiiOptions { AllowList = ["Zephyrix", "Orbit Suite"] });
    private static readonly PiiDetector Strict = new(new PiiOptions { AllowList = ["Zephyrix", "Orbit Suite"], FailClosed = true });

    private static string Mask(string text) => Detector.Mask(text).MaskedText;

    // ---- email
    [Theory]
    [InlineData("write to anna.kowalska@example.com now", "write to [EMAIL] now")]
    [InlineData("mail: jan_nowak91@poczta.example.", "mail: [EMAIL].")]
    [InlineData("o'brien@corp.example and x+tag@sub.domain.example.pl", "[EMAIL] and [EMAIL]")]
    [InlineData("pisz: t.wisniewski[at]mailbox.example", "pisz: [EMAIL]")]
    [InlineData("zażółć@gęślą.example", "[EMAIL]")]
    public void Emails_are_masked(string input, string expected) => Assert.Equal(expected, Mask(input));

    [Theory]
    [InlineData("the ratio was 3@5 in the end")]
    [InlineData("ask support@helpdesk about it")]
    [InlineData("see @ the door")]
    public void Things_that_are_not_emails_are_left_alone(string input) => Assert.Equal(input, Mask(input));

    // ---- url, handle, ip
    [Theory]
    [InlineData("see https://janek.example/work?x=1, please", "see [URL], please")]
    [InlineData("go to www.zephyrix.example/careers.", "go to [URL].")]
    [InlineData("on medium.com/@someone/why-i-left today", "on [URL] today")]
    [InlineData("linkedin.com/in/ewa-malinowska-4521", "[URL]")]
    [InlineData("HTTP://UPPER.EXAMPLE/PATH", "[URL]")]
    public void Urls_are_masked(string input, string expected) => Assert.Equal(expected, Mask(input));

    [Theory]
    [InlineData("We run ASP.NET and Node.js.")]
    [InlineData("Next.js and Vue.js apps")]
    [InlineData("e.g. the file notes.txt")]
    public void Technology_names_are_not_urls(string input) => Assert.Equal(input, Mask(input));

    [Theory]
    [InlineData("find me on @ania.codes ok", "find me on [HANDLE] ok")]
    [InlineData("he is @mkowalski_dev", "he is [HANDLE]")]
    [InlineData("telegram: ewa_telegram_22", "telegram: [HANDLE]")]
    [InlineData("github/ewa-dev", "github/[HANDLE]")]
    public void Handles_are_masked(string input, string expected) => Assert.Equal(expected, Mask(input));

    [Theory]
    [InlineData("server 10.20.30.40 was down", "server [IP_ADDRESS] was down")]
    [InlineData("from 2001:0db8:85a3:0000:0000:8a2e:0370:7334 today", "from [IP_ADDRESS] today")]
    public void Ip_addresses_are_masked(string input, string expected) => Assert.Equal(expected, Mask(input));

    [Theory]
    [InlineData("shipped version 2.14.7.3 in Q2")]
    [InlineData("build 10.2.3.4 failed")]
    public void Version_numbers_are_not_ip_addresses(string input) => Assert.Equal(input, Mask(input));

    // ---- phone
    [Theory]
    [InlineData("call +48 512 345 678 tomorrow", "call [PHONE] tomorrow")]
    [InlineData("601-222-333", "[PHONE]")]
    [InlineData("zadzwoń (22) 123-45-67.", "zadzwoń [PHONE].")]
    [InlineData("moja komórka 600 700 800", "moja komórka [PHONE]")]
    [InlineData("+44 7911 123456 was in the signature", "[PHONE] was in the signature")]
    [InlineData("0048 501 502 503", "[PHONE]")]
    [InlineData("tel: 123 4567", "tel: [PHONE]")]
    [InlineData("phone 555-0199", "phone [PHONE]")]
    public void Phone_numbers_are_masked(string input, string expected) => Assert.Equal(expected, Mask(input));

    [Theory]
    [InlineData("I joined on 2021-03-15 and left 15.03.2024")]
    [InlineData("salary was 12 500 PLN and bonus 8 000 PLN")]
    [InlineData("they offered 10 000 - 12 000 PLN")]
    [InlineData("revenue 1 450 000 000 last year")]
    [InlineData("extension 4401 on floor 12")]
    [InlineData("meetings at 10:30 and 14:00")]
    [InlineData("headcount 3 400 and 3 900")]
    public void Dates_money_and_ordinary_numbers_are_not_phone_numbers(string input) => Assert.Equal(input, Mask(input));

    // ---- national and financial ids
    [Fact]
    public void A_pesel_with_a_valid_checksum_is_masked_by_default_and_an_invalid_one_still_is_as_a_number()
    {
        Assert.True(PatternRules.PeselValid("85061543217"));
        Assert.False(PatternRules.PeselValid("85061543218"));
        Assert.Equal("my pesel [NATIONAL_ID] ok", Mask("my pesel 85061543217 ok"));
        Assert.DoesNotContain("85061543218", Mask("my pesel 85061543218 ok"));
    }

    [Fact]
    public void An_eleven_digit_run_is_masked_as_a_national_id_even_with_a_bad_checksum()
    {
        var finding = Assert.Single(Detector.Detect("number 12345678901 here"));

        Assert.Equal(PiiKind.NationalId, finding.Kind);
        Assert.Equal("number [NATIONAL_ID] here", Mask("number 12345678901 here"));
    }

    [Fact]
    public void Fail_closed_also_masks_short_digit_runs_that_could_be_local_phone_numbers()
    {
        Assert.Equal("ticket 4482915 closed", Mask("ticket 4482915 closed"));
        Assert.Equal("ticket [PHONE] closed", Strict.Mask("ticket 4482915 closed").MaskedText);
    }

    [Theory]
    [InlineData("their NIP was 526-104-08-11 on the invoice", "their NIP was [NATIONAL_ID] on the invoice")]
    [InlineData("NIP: 5261040811 appeared", "NIP: [NATIONAL_ID] appeared")]
    [InlineData("ID card ABC 123456 was copied", "ID card [NATIONAL_ID] was copied")]
    [InlineData("passport AB1234567 scanned", "passport [NATIONAL_ID] scanned")]
    [InlineData("SSN 123-45-6789 requested", "SSN [NATIONAL_ID] requested")]
    [InlineData("konto 61 1090 1014 0000 0712 1981 2874 na przelewy", "konto [NATIONAL_ID] na przelewy")]
    [InlineData("IBAN PL61109010140000071219812874", "IBAN [NATIONAL_ID]")]
    [InlineData("NINO AB 12 34 56 C", "NINO [NATIONAL_ID]")]
    [InlineData("REGON: 123456785", "REGON: [NATIONAL_ID]")]
    public void National_and_financial_ids_are_masked(string input, string expected) => Assert.Equal(expected, Mask(input));

    [Fact]
    public void Nip_checksum_is_computed()
    {
        Assert.True(PatternRules.NipValid("5261040811"));
        Assert.False(PatternRules.NipValid("5261040812"));
        Assert.False(PatternRules.NipValid("12345"));
    }

    [Theory]
    [InlineData("the offer said PLN 150000 gross per year")]
    [InlineData("bonus of EUR 120000 in writing")]
    public void A_currency_code_followed_by_a_number_is_not_an_id_card(string input) => Assert.Equal(input, Mask(input));

    // ---- employee ids
    [Theory]
    [InlineData("My employee ID was E-100234 on the badge", "My [EMPLOYEE_ID] on the badge")]
    [InlineData("employee number 48213 was used", "employee number [EMPLOYEE_ID] was used")]
    [InlineData("Numer pracownika: PR-5520.", "Numer pracownika: [EMPLOYEE_ID].")]
    [InlineData("badge EMP-77120 stopped working", "badge [EMPLOYEE_ID] stopped working")]
    [InlineData("Staff ID: SA00912 on the laptop", "Staff ID: [EMPLOYEE_ID] on the laptop")]
    [InlineData("her badge was ID-7740213", "her badge was [EMPLOYEE_ID]")]
    public void Employee_ids_are_masked(string input, string expected)
    {
        var masked = Mask(input);
        Assert.Contains("[EMPLOYEE_ID]", masked);
        Assert.Equal(expected, masked.Replace("My employee ID was [EMPLOYEE_ID]", "My [EMPLOYEE_ID]"));
    }

    // ---- names of individuals
    [Theory]
    [InlineData("My manager Tomasz Zieliński cancelled every one-to-one.", "My manager [PERSON] cancelled every one-to-one.")]
    [InlineData("My boss, Joanna Kamińska, never answered.", "My boss, [PERSON], never answered.")]
    [InlineData("Our director Hartmann rarely visited.", "Our director [PERSON] rarely visited.")]
    [InlineData("Anna Wójcik, my manager, changed the targets.", "[PERSON], my manager, changed the targets.")]
    [InlineData("Dr. Weronika Szymańska ran the department.", "Dr. [PERSON] ran the department.")]
    [InlineData("Mrs Fitzgerald from legal sent it.", "Mrs [PERSON] from legal sent it.")]
    [InlineData("Pan Mazurek z działu sprzedaży był niemiły.", "Pan [PERSON] z działu sprzedaży był niemiły.")]
    [InlineData("Mój szef Marek Jankowski nie rozmawiał z nami.", "Mój szef [PERSON] nie rozmawiał z nami.")]
    [InlineData("Z moim kierownikiem Dariuszem Michalskim nie dało się rozmawiać.", "Z moim kierownikiem [PERSON] nie dało się rozmawiać.")]
    [InlineData("I reported directly to Elena Voronova for two years.", "I reported directly to [PERSON] for two years.")]
    [InlineData("A guy named Stefan took over.", "A guy named [PERSON] took over.")]
    public void Names_after_a_cue_are_masked(string input, string expected) => Assert.Equal(expected, Mask(input));

    [Theory]
    [InlineData("It was Marcin Kaczmarek who decided.", "It was [PERSON] who decided.")]
    [InlineData("Jan van der Meer joined as head of delivery.", "[PERSON] joined as head of delivery.")]
    [InlineData("The decision came from J. Brandt himself.", "The decision came from [PERSON] himself.")]
    [InlineData("Ask Agnieszka about the budget.", "Ask [PERSON] about the budget.")]
    [InlineData("Rozmawiałem z Kasią o urlopie.", "Rozmawiałem z [PERSON] o urlopie.")]
    [InlineData("Pracowałem z Anną i Markiem.", "Pracowałem z [PERSON] i [PERSON].")]
    [InlineData("Rozmawiałem z Pawłem o awansie.", "Rozmawiałem z [PERSON] o awansie.")]
    [InlineData("Her surname was Przybylska and she was in finance.", "Her surname was [PERSON] and she was in finance.")]
    [InlineData("Thanks to Piotr the first weeks were fine.", "Thanks to [PERSON] the first weeks were fine.")]
    public void Capitalised_runs_given_names_and_polish_surname_shapes_are_masked(string input, string expected) => Assert.Equal(expected, Mask(input));

    [Theory]
    [InlineData("I joined Zephyrix in March and left in September.")]
    [InlineData("Orbit Suite was the main tool.")]
    [InlineData("We used Microsoft Teams, Jira and Confluence.")]
    [InlineData("The Warsaw office closed in June.")]
    [InlineData("Human Resources never replied to the Product Manager.")]
    [InlineData("On Monday the CEO announced it.")]
    [InlineData("Polish and English were both used.")]
    [InlineData("I'm sure I've seen it; I'll check.")]
    [InlineData("Yes. No. Maybe.")]
    [InlineData("In Poland the market was smaller.")]
    [InlineData("Dział HR nigdy nie odpowiedział.")]
    [InlineData("Interviewer: And your manager?\nUser: Honestly, nothing.")]
    public void Ordinary_capitalised_words_are_not_names(string input) => Assert.Equal(input, Mask(input));

    [Fact]
    public void The_allow_list_keeps_employer_and_product_names_out_of_name_runs()
    {
        var text = "At Zephyrix Labs the Orbit Suite was slow.";

        Assert.Equal(text, Mask(text));
        Assert.Contains("[PERSON]", new PiiDetector().Mask(text).MaskedText);
        Assert.Contains("[PERSON]", new PiiDetector().Mask("Tomasz Zephyrix joined.").MaskedText);
        Assert.Equal("Tomasz Zephyrix joined.", new PiiDetector(new PiiOptions { AllowList = ["Tomasz", "Zephyrix"] }).Mask("Tomasz Zephyrix joined.").MaskedText);
    }

    [Fact]
    public void The_allow_list_matches_case_insensitively_with_a_short_ending_for_longer_entries()
    {
        var d = new PiiDetector(new PiiOptions { AllowList = ["Zephyrix"], FailClosed = true });

        Assert.Equal("Pracuję w zephyrix i w Zephyrixie.", d.Mask("Pracuję w zephyrix i w Zephyrixie.").MaskedText);
    }

    [Fact]
    public void The_allow_list_does_not_unmask_an_email_or_url_containing_the_name()
    {
        var d = new PiiDetector(new PiiOptions { AllowList = ["Zephyrix"] });

        Assert.Equal("[EMAIL] and [URL]", d.Mask("hr@zephyrix.example and www.zephyrix.example/x").MaskedText);
    }

    [Fact]
    public void Ambiguous_given_names_are_masked_mid_sentence_but_only_fail_closed_at_the_start()
    {
        Assert.Equal("I asked [PERSON] about it.", Mask("I asked Mark about it."));
        Assert.Equal("Will you help? Mark my words.", Mask("Will you help? Mark my words."));
        Assert.Contains("[PERSON]", Strict.Mask("Will you help? Mark my words.").MaskedText);
    }

    [Fact]
    public void Fail_closed_masks_unknown_capitalised_words_in_the_middle_of_a_sentence()
    {
        Assert.Equal("Thanks to Aisha and Dmitri it was fine.", Mask("Thanks to Aisha and Dmitri it was fine."));
        var closed = Strict.Mask("Thanks to Aisha and Dmitri it was fine.");

        Assert.Equal("Thanks to [PERSON] and [PERSON] it was fine.", closed.MaskedText);
        Assert.All(closed.Findings, f => Assert.Equal(PiiBasis.FailClosed, f.Basis));
    }

    // ---- findings, offsets, masking mechanics
    [Fact]
    public void Findings_carry_kind_and_offsets_into_the_original_text_and_never_the_text()
    {
        var text = "Mail anna@x.example or call 601 222 333.";

        var findings = Detector.Detect(text);

        Assert.Equal([PiiKind.Email, PiiKind.Phone], findings.Select(f => f.Kind));
        Assert.Equal("anna@x.example", text.Substring(findings[0].Start, findings[0].Length));
        Assert.Equal("601 222 333", text.Substring(findings[1].Start, findings[1].Length));
        Assert.DoesNotContain(typeof(PiiFinding).GetProperties(BindingFlags.Public | BindingFlags.Instance), p => p.PropertyType == typeof(string));
        Assert.DoesNotContain(typeof(PiiFinding).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic), f => f.FieldType == typeof(string));
        Assert.DoesNotContain("anna", findings[0].ToString());
    }

    [Fact]
    public void Offsets_are_utf16_units_so_surrogate_pairs_before_a_finding_are_counted_correctly()
    {
        var text = "😀😀 mail a@b.example";

        var f = Assert.Single(Detector.Detect(text));

        Assert.Equal("a@b.example", text.Substring(f.Start, f.Length));
        Assert.Equal("😀😀 mail [EMAIL]", Mask(text));
    }

    [Fact]
    public void Overlapping_candidates_are_merged_into_their_union_so_nothing_is_left_behind()
    {
        // An email local part that also looks like a phone number: one finding, kind of the more specific rule.
        var f = Assert.Single(Detector.Detect("600700800@example.com"));

        Assert.Equal(PiiKind.Email, f.Kind);
        Assert.Equal("[EMAIL]", Mask("600700800@example.com"));
    }

    [Fact]
    public void Masking_is_idempotent_and_deterministic_over_the_corpora()
    {
        foreach (var corpus in new[] { "dev.txt", "heldout.txt" })
            foreach (var s in Corpus.Load(corpus))
            {
                var once = Strict.Mask(s.Text);
                Assert.Equal(once.MaskedText, Strict.Mask(s.Text).MaskedText);
                Assert.Empty(Strict.Detect(once.MaskedText));
            }
    }

    [Fact]
    public void Masked_text_contains_none_of_the_gold_pii_in_fail_closed_mode()
    {
        foreach (var s in Corpus.Load("dev.txt").Concat(Corpus.Load("heldout.txt")))
        {
            var masked = Strict.Mask(s.Text).MaskedText;
            foreach (var g in s.Gold.Where(g => g.Kind is "email" or "phone" or "url" or "id" or "emp" or "ip" or "handle"))
                Assert.DoesNotContain(s.Text.Substring(g.Start, g.Length), masked);
        }
    }

    [Fact]
    public void Empty_text_is_fine_and_null_is_rejected()
    {
        Assert.Empty(Detector.Detect(""));
        Assert.Equal("", Mask(""));
        Assert.Throws<ArgumentNullException>(() => Detector.Detect(null!));
    }

    [Fact]
    public void Mask_returns_the_same_instance_text_when_nothing_is_found()
    {
        const string clean = "Nothing personal in here at all.";

        Assert.Same(clean, Detector.Mask(clean).MaskedText);
        Assert.False(Detector.Mask(clean).HasFindings);
    }

    [Fact]
    public void Placeholders_are_distinct_and_cover_every_kind()
    {
        var all = Enum.GetValues<PiiKind>().Select(PiiDetector.Placeholder).ToArray();

        Assert.Equal(all.Length, all.Distinct().Count());
        Assert.All(all, p => Assert.Matches(@"^\[[A-Z_]+\]$", p));
    }

    // ---- adversarial input
    [Theory]
    [InlineData("a.")]
    [InlineData("@")]
    [InlineData("1")]
    [InlineData("A")]
    [InlineData("- ")]
    public void Pathological_input_finishes_quickly(string unit)
    {
        var text = string.Concat(Enumerable.Repeat(unit, 40_000));
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _ = Strict.Mask(text);

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
    }

    [Fact]
    public void A_realistic_long_transcript_is_processed_in_reasonable_time()
    {
        var text = string.Concat(Enumerable.Repeat("Interviewer: And how was onboarding?\nUser: My manager Tomasz Zieliński never replied; write to a@b.example.\n", 2000));
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var r = Detector.Mask(text);

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
        Assert.DoesNotContain("Zieliński", r.MaskedText);
        Assert.DoesNotContain("a@b.example", r.MaskedText);
    }

    [Fact]
    public void Dependencies_are_the_framework_only()
    {
        var refs = typeof(PiiDetector).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(refs, r => r.StartsWith("ExitInterviewAgent", StringComparison.Ordinal) || r.Contains("Http", StringComparison.Ordinal) || r.Contains("Sockets", StringComparison.Ordinal));
        Assert.Empty(Regex.Matches(File.ReadAllText(Path.Combine(Fixtures.SourceDir, "PiiDetector.cs")), "HttpClient|Socket|File\\."));
    }
}

internal static class Fixtures
{
    public static string SourceDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExitInterviewAgent.sln"))) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "src", "ExitInterviewAgent.Privacy");
        }
    }
}
