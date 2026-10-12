using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tests.Protocol;

/// <summary>Protocol 1.3 (interview-dynamics spec §4.3, WP1): the fixed texts, the register tokens and the parse of the 1.3 fields.</summary>
public class Protocol13Tests
{
    private static readonly InterviewProtocol Pl = InterviewProtocol.For("pl");
    private static readonly InterviewProtocol En = InterviewProtocol.Current;
    private static readonly PiiGuard Pii = new(["Widgetron"]);

    private static readonly string[] MoveKeys = ["warmup", "example", "elaborate", "ladder", "consequence", "scale", "scaleFollowUp", "retention", "anythingElse"];
    private static readonly string[] PrefaceKeys = ["checkIn", "alreadySaid", "support", "legalNotice"];

    private static readonly Regex GenderedPastTense = new(@"\b\w+ł(a|em|am|eś|aś)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AnyToken = new(@"\{Pan(a|u)?\}", RegexOptions.CultureInvariant);

    private static InterviewProtocol Lang(string language) => InterviewProtocol.For(language);

    [Fact]
    public void Both_shipped_protocols_are_version_1_3()
    {
        Assert.Equal("1.3", Pl.ProtocolVersion);
        Assert.Equal("1.3", En.ProtocolVersion);
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void Every_fixed_text_passes_the_leading_lint_in_every_register_form(string language)
    {
        var p = Lang(language);
        Assert.NotEmpty(p.FixedTexts);
        foreach (var (key, text) in p.FixedTexts)
            foreach (var form in Enum.GetValues<AddressForm>())
                Assert.True(QuestionGuard.LeadingReason(p.Render(text, form)) is null, $"{key} ({form}) trips the leading lint");
    }

    [Fact]
    public void The_checkin_preface_quotes_a_phrase_that_the_finish_request_cue_recognises()
    {
        var quoted = Regex.Match(Pl.Prefaces["checkIn"].Text, "„([^”]+)”").Groups[1].Value;

        Assert.False(string.IsNullOrWhiteSpace(quoted));
        Assert.True(ReplyAnalyzer.Analyze(quoted, Pl.Limits, false).FinishRequest, $"'{quoted}' is not a finish request");
    }

    [Fact]
    public void The_english_checkin_preface_quotes_a_phrase_that_the_finish_request_cue_recognises()
    {
        var quoted = Regex.Match(En.Prefaces["checkIn"].Text, "\"([^\"]+)\"").Groups[1].Value;

        Assert.True(ReplyAnalyzer.Analyze(quoted, En.Limits, false).FinishRequest, $"'{quoted}' is not a finish request");
    }

    [Fact]
    public void No_polish_fixed_text_has_a_gendered_past_tense_for_the_person_or_the_ai()
    {
        // The ty variants are excluded on purpose: the gendered-free rule applies to the Pan, Pani and neutral renderings.
        foreach (var (key, text) in Pl.FixedTexts)
            foreach (var form in new[] { AddressForm.Neutral, AddressForm.Pan, AddressForm.Pani })
                Assert.False(GenderedPastTense.IsMatch(Pl.Render(text, form)), $"{key} ({form}) has a gendered past-tense form");
    }

    [Fact]
    public void Polish_fixed_texts_use_lub_for_the_neutral_form_and_never_a_slash()
    {
        foreach (var (key, text) in Pl.FixedTexts)
        {
            Assert.DoesNotContain("Pan/Pani", text.Text);
            Assert.DoesNotContain("Pana/Pani", text.Text);
            Assert.DoesNotContain("Panu/Pani", text.Text);
            Assert.DoesNotContain("Pan/Pan", text.Text);
            Assert.DoesNotContain("Pan/Pani", key);
        }
    }

    [Fact]
    public void Every_text_with_register_tokens_has_a_ty_variant_and_the_variant_has_no_tokens()
    {
        foreach (var p in new[] { Pl, En })
            foreach (var (key, text) in p.FixedTexts)
            {
                if (!AnyToken.IsMatch(text.Text)) continue;
                Assert.False(string.IsNullOrWhiteSpace(text.Ty), $"{p.Language}: {key} has register tokens and no ty variant");
                Assert.False(AnyToken.IsMatch(text.Ty!), $"{p.Language}: the ty variant of {key} still has tokens");
            }
    }

    [Fact]
    public void Register_tokens_render_as_pan_pani_or_both_in_the_neutral_form()
    {
        const string text = "Czy zgadza się {Pan} na to? Z {Pana} słów. Dla {Panu}.";

        Assert.Equal("Czy zgadza się Pan na to? Z Pana słów. Dla Panu.", Pl.Render(text, AddressForm.Pan));
        Assert.Equal("Czy zgadza się Pani na to? Z Pani słów. Dla Pani.", Pl.Render(text, AddressForm.Pani));
        Assert.Equal("Czy zgadza się Pan lub Pani na to? Z Pana lub Pani słów. Dla Panu lub Pani.", Pl.Render(text, AddressForm.Neutral));
    }

    [Fact]
    public void A_text_without_tokens_renders_unchanged_in_every_form()
    {
        const string text = "Dziękuję za czas.";

        Assert.All(Enum.GetValues<AddressForm>(), form => Assert.Equal(text, Pl.Render(text, form)));
    }

    [Fact]
    public void Rendering_a_token_text_in_the_ty_form_without_a_ty_variant_is_refused() =>
        Assert.Throws<ArgumentException>(() => Pl.Render("Czy zgadza się {Pan}?", AddressForm.Ty));

    [Fact]
    public void A_ty_form_fixed_text_uses_the_variant_and_not_the_token_text()
    {
        var checkIn = Pl.FixedTexts.Single(f => f.Key == "prefaces.checkIn").Text;
        var rendered = Pl.Render(checkIn, AddressForm.Ty);

        Assert.Equal(checkIn.Ty, rendered);
        Assert.Contains("wolisz", rendered);
        Assert.DoesNotContain("{Pan", rendered);
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void The_moves_and_prefaces_are_complete(string language)
    {
        var p = Lang(language);

        Assert.Equal(MoveKeys.Order(), p.Moves.Keys.Order());
        Assert.Equal(PrefaceKeys.Order(), p.Prefaces.Keys.Order());
        Assert.All(p.Moves.Values, m => Assert.False(string.IsNullOrWhiteSpace(m.Text)));
        Assert.All(p.Prefaces.Values, m => Assert.False(string.IsNullOrWhiteSpace(m.Text)));
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void Every_topic_but_the_reason_for_leaving_has_a_scale_label(string language)
    {
        var p = Lang(language);

        foreach (var spec in p.Topics)
            if (spec.Topic == Topic.ReasonForLeaving) Assert.Equal("", spec.ScaleLabel);
            else Assert.False(string.IsNullOrWhiteSpace(spec.ScaleLabel), $"{language}: {spec.Id} has no scale label");
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void The_scale_question_names_the_topic_and_passes_the_scale_rules(string language)
    {
        var p = Lang(language);

        foreach (var spec in p.Topics.Where(t => t.Topic != Topic.ReasonForLeaving))
        {
            var q = p.ScaleQuestion(spec, AddressForm.Neutral);

            Assert.DoesNotContain("{scaleLabel}", q);
            Assert.Contains(spec.ScaleLabel, q);
            Assert.Equal("ok", QuestionGuard.Check(q, TurnKind.Topic, Pii, QuestionShape.Scale).Reason);
        }
    }

    [Fact]
    public void The_polish_moves_are_the_spec_wording_for_the_reason_question_and_example()
    {
        Assert.Equal("Jaka jedna konkretna sytuacja najlepiej to pokazuje?", Pl.Moves["example"].Text);
        Assert.Equal(Pl.Moves["example"].Text, Pl.Probe);
        Assert.Equal("Jak to się stało, że pojawił się pomysł odejścia?", Pl.Topics.Single(t => t.Topic == Topic.ReasonForLeaving).Question);
    }

    [Fact]
    public void The_1_3_limits_carry_the_spec_values()
    {
        var l = Pl.Limits;

        Assert.Equal(2, l.MaxFollowUpsCore);
        Assert.Equal(2, l.MaxFollowUpsReason);
        Assert.Equal(1, l.MaxFollowUpsLight);
        Assert.Equal(12, l.SubstantiveWords);
        Assert.Equal(3, l.MaxCoreTopics);
        Assert.Equal(14, l.TargetQuestions);
        Assert.Equal(24, l.MaxQuestions);
        Assert.Equal(7, l.CheckInAfterQuestions);
        // WP5 moves the two that the machine reads (spec §3.4 deepening limit 3, §3.5 interviewer turns 40).
        Assert.Equal(40, l.MaxInterviewerTurns);
        Assert.Equal(3, l.MaxDeepProbesPerTopic);
    }

    [Fact]
    public void A_1_2_protocol_file_still_loads_and_takes_the_1_3_limit_defaults()
    {
        var p = InterviewProtocol.Parse(ReadFixture("protocol-1.2.en.json"));

        Assert.Equal("1.2", p.ProtocolVersion);
        Assert.Empty(p.Moves);
        Assert.Empty(p.Prefaces);
        Assert.Equal(2, p.Limits.MaxFollowUpsCore);
        Assert.Equal(14, p.Limits.TargetQuestions);
        Assert.StartsWith("Could you give me one specific example", p.Probe);
        Assert.Throws<InvalidOperationException>(() => p.ScaleQuestion(p.Topics[0], AddressForm.Neutral));
    }

    [Fact]
    public void A_file_with_a_partial_move_set_is_refused()
    {
        var node = JsonNode.Parse(ReadFixture("protocol-1.2.en.json"))!.AsObject();
        node["moves"] = new JsonObject { ["warmup"] = "Warm up?" };

        Assert.Throws<InvalidDataException>(() => InterviewProtocol.Parse(node.ToJsonString()));
    }

    [Fact]
    public void A_fixed_text_with_a_register_token_and_no_ty_variant_is_refused()
    {
        var node = JsonNode.Parse(ReadFixture("protocol-1.2.en.json"))!.AsObject();
        node["consentReask"] = "Czy zgadza się {Pan} na to?";

        Assert.Throws<InvalidDataException>(() => InterviewProtocol.Parse(node.ToJsonString()));
    }

    [Fact]
    public void A_ty_variant_for_a_text_that_does_not_exist_is_refused()
    {
        var node = JsonNode.Parse(ReadFixture("protocol-1.2.en.json"))!.AsObject();
        node["tyVariants"] = new JsonObject { ["moves.nothing"] = "Nie." };

        Assert.Throws<InvalidDataException>(() => InterviewProtocol.Parse(node.ToJsonString()));
    }

    private static string ReadFixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", name));
}
