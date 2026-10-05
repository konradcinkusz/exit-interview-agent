using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Tracing;
using ExitInterviewAgent.Records;
using ExitInterviewAgent.TestSupport;

namespace ExitInterviewAgent.Agent.Tests.Architecture;

/// <summary>Boundaries of the Agent project, enforced so that they cannot drift without a failing build.</summary>
public class AgentArchitectureTests
{
    private static readonly Assembly Agent = typeof(InterviewProtocol).Assembly;

    private static string RepoRoot => ExitInterviewAgent.Agent.Tests.Tracing.TraceTests.RepoRoot();

    private static readonly string[] Forbidden =
    [
        "ExitInterviewAgent.ServiceDefaults", "ExitInterviewAgent.InterviewService", "ExitInterviewAgent.Contracts",
        "ExitInterviewAgent.AppHost", "ExitInterviewAgent.Personas", "ExitInterviewAgent.Cli",
    ];

    [Fact]
    public void The_agent_assembly_does_not_reference_the_kernel_the_services_the_web_layer_or_its_own_consumers()
    {
        var referenced = Agent.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.Empty(referenced.Intersect(Forbidden));
        Assert.DoesNotContain(referenced, n => n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) || n.StartsWith("Aspire", StringComparison.Ordinal) || n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void The_agent_project_file_has_no_project_reference_beyond_records_and_privacy()
    {
        var csproj = XDocument.Load(Path.Combine(RepoRoot, "src", "ExitInterviewAgent.Agent", "ExitInterviewAgent.Agent.csproj"));

        var projects = csproj.Descendants("ProjectReference").Select(e => Path.GetFileNameWithoutExtension(((string)e.Attribute("Include")!).Replace('\\', '/'))).Order().ToList();

        Assert.Equal(["ExitInterviewAgent.Privacy", "ExitInterviewAgent.Records"], projects);
    }

    [Fact]
    public void The_agent_makes_no_network_calls_of_its_own_it_references_no_http_client_assembly()
    {
        // Provider clients arrive with T6, in their own project; the agent core and the mock stay offline.
        var referenced = Agent.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.DoesNotContain(referenced, n => n is "System.Net.Http" or "System.Net.Sockets" or "System.Net.Requests" or "System.Net.WebClient");
        Assert.DoesNotContain(Agent.GetTypes().SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)), f => f.FieldType.FullName == "System.Net.Http.HttpClient");
    }

    [Fact]
    public void Only_the_agent_project_may_be_depended_on_by_personas_and_cli_never_the_other_way_round()
    {
        foreach (var (project, mustNotReference) in new[] { ("ExitInterviewAgent.Records", "Agent"), ("ExitInterviewAgent.Privacy", "Agent"), ("ExitInterviewAgent.Agent", "Personas"), ("ExitInterviewAgent.Agent", "Cli"), ("ExitInterviewAgent.Personas", "Cli") })
        {
            var csproj = File.ReadAllText(Path.Combine(RepoRoot, "src", project, project + ".csproj"));
            Assert.DoesNotContain($"ExitInterviewAgent.{mustNotReference}.csproj", csproj);
        }
    }

    [Fact]
    public void The_kernel_stays_free_of_agent_domain_code()
    {
        var kernel = Directory.GetFiles(Path.Combine(RepoRoot, "src", "ExitInterviewAgent.ServiceDefaults"), "*.cs", SearchOption.AllDirectories);
        foreach (var f in kernel)
        {
            var text = File.ReadAllText(f);
            Assert.DoesNotContain("InterviewProtocol", text);
            Assert.DoesNotContain("IChatClient", text);
            Assert.DoesNotContain("ExitInterviewAgent.Agent", text);
        }
    }

    [Fact]
    public void The_extractor_schema_has_no_identifier_timestamp_emotion_sentiment_or_affect_field_using_the_same_list_as_the_record_schema()
    {
        var names = PropertyNames(JsonDocument.Parse(ExtractorOutput.SchemaText).RootElement).ToList();

        Assert.NotEmpty(names);
        Assert.Empty(ForbiddenFieldNames.Violations(names));
    }

    [Fact]
    public void The_forbidden_word_guard_can_fail_for_the_extractor_schema()
    {
        var mutated = ExtractorOutput.SchemaText.Replace("\"rating\"", "\"sentiment\"");

        var names = PropertyNames(JsonDocument.Parse(mutated).RootElement).ToList();

        Assert.Contains("sentiment", ForbiddenFieldNames.Violations(names));
    }

    [Fact]
    public void Every_object_in_the_extractor_schema_is_closed()
    {
        var open = new List<string>();
        void Walk(JsonElement e, string path)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                if (e.TryGetProperty("properties", out _) && !(e.TryGetProperty("additionalProperties", out var ap) && ap.ValueKind == JsonValueKind.False)) open.Add(path);
                foreach (var p in e.EnumerateObject().Where(p => p.Name is not ("if" or "then" or "else" or "not"))) Walk(p.Value, path + "/" + p.Name);
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var x in e.EnumerateArray()) Walk(x, path + "/" + i++);
            }
        }

        Walk(JsonDocument.Parse(ExtractorOutput.SchemaText).RootElement, "#");

        Assert.Empty(open);
    }

    [Fact]
    public void The_extractor_schema_topics_are_exactly_the_record_topics()
    {
        var topics = JsonDocument.Parse(ExtractorOutput.SchemaText).RootElement.GetProperty("properties").GetProperty("topics").GetProperty("properties").EnumerateObject().Select(p => p.Name);

        Assert.Equal(Wire.Names<Topic>(), topics);
    }

    [Fact]
    public void The_published_extractor_schema_file_is_the_embedded_one()
    {
        var onDisk = File.ReadAllText(Path.Combine(RepoRoot, "schemas", "extractor-output.v1.schema.json"));

        Assert.Equal(onDisk, ExtractorOutput.SchemaText);
    }

    [Fact]
    public void Span_and_attribute_names_follow_the_naming_rules()
    {
        foreach (var t in new[] { typeof(InterviewTelemetry.Attr), typeof(InterviewTelemetry.Events), typeof(InterviewTelemetry.Spans) })
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral))
                Assert.Matches("^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)*$", (string)f.GetRawConstantValue()!);
    }

    private static IEnumerable<string> PropertyNames(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name == "properties" && p.Value.ValueKind == JsonValueKind.Object)
                    foreach (var prop in p.Value.EnumerateObject()) { yield return prop.Name; foreach (var n in PropertyNames(prop.Value)) yield return n; }
                else
                    foreach (var n in PropertyNames(p.Value)) yield return n;
            }
        else if (e.ValueKind == JsonValueKind.Array)
            foreach (var x in e.EnumerateArray()) foreach (var n in PropertyNames(x)) yield return n;
    }
}
