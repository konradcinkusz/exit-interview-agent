using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.InterviewService.Mcp;
using ExitInterviewAgent.InterviewService.Tests.Support;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace ExitInterviewAgent.InterviewService.Tests.Mcp;

/// <summary>
/// The MCP contract, pinned. Tool and prompt descriptions are read by the host model as instructions, so a change to any
/// word of them (or to a schema, an annotation, a resource, or the generated prompt text) fails this test until a person
/// regenerates the snapshot and the diff is reviewed in the PR:
/// <c>UPDATE_MCP_SNAPSHOT=1 dotnet test --filter McpContractSnapshotTests</c>. Long texts are pinned by SHA-256 so the diff
/// shows THAT they changed; the text itself is in the source files a reviewer is already reading.
/// </summary>
public sealed class McpContractSnapshotTests
{
    private static string SnapshotPath([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "mcp-contract.snapshot.json");

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    private static JsonNode Node<T>(T value) => JsonSerializer.SerializeToNode(value, McpJsonUtilities.DefaultOptions)!;

    private static async Task<string> BuildAsync()
    {
        using var host = new TestHost();
        await using var client = await McpTestClient.ConnectAsync(host.Client(host.McpToken("account-1")));

        var tools = (await client.ListToolsAsync()).OrderBy(t => t.Name).Select(t => Node(t.ProtocolTool)).ToArray();
        var prompts = (await client.ListPromptsAsync()).OrderBy(p => p.Name).Select(p => Node(p.ProtocolPrompt)).ToArray();
        var resources = (await client.ListResourcesAsync()).OrderBy(r => r.Uri).Select(r => Node(r.ProtocolResource)).ToArray();

        var promptText = async (Dictionary<string, object?> args) =>
            Assert.IsType<TextContentBlock>((await client.GetPromptAsync(McpNames.ConductPrompt, args)).Messages.Single().Content).Text;
        var resourceHashes = new JsonObject();
        foreach (var r in resources)
        {
            var uri = r["uri"]!.GetValue<string>();
            resourceHashes[uri] = Sha(Assert.IsType<TextResourceContents>((await client.ReadResourceAsync(uri)).Contents.Single()).Text);
        }

        var snapshot = new JsonObject
        {
            ["protocolRevisionsAccepted"] = new JsonArray(McpTransportGuard.SupportedProtocolVersions.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
            ["serverInfo"] = new JsonObject { ["name"] = client.ServerInfo.Name, ["version"] = client.ServerInfo.Version },
            ["instructions"] = client.ServerInstructions,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = client.ServerCapabilities.Tools is not null,
                ["prompts"] = client.ServerCapabilities.Prompts is not null,
                ["resources"] = client.ServerCapabilities.Resources is not null,
            },
            ["tools"] = new JsonArray(tools),
            ["prompts"] = new JsonArray(prompts),
            ["resources"] = new JsonArray(resources),
            ["resourceTextSha256"] = resourceHashes,
            ["promptTextSha256"] = new JsonObject
            {
                ["default"] = Sha(await promptText([])),
                ["pl+hint"] = Sha(await promptText(new() { ["language"] = "pl", ["employerHint"] = "Acme Sp. z o.o." })),
            },
            ["protocolVersion"] = InterviewProtocol.Current.ProtocolVersion,
        };
        return snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    }

    [Fact]
    public async Task The_tool_prompt_and_resource_listings_match_the_reviewed_snapshot()
    {
        var actual = await BuildAsync();
        if (Environment.GetEnvironmentVariable("UPDATE_MCP_SNAPSHOT") == "1")
        {
            File.WriteAllText(SnapshotPath(), actual);
            return;
        }

        Assert.True(File.Exists(SnapshotPath()), "mcp-contract.snapshot.json is missing; generate it with UPDATE_MCP_SNAPSHOT=1 and review it.");
        Assert.Equal(File.ReadAllText(SnapshotPath()).ReplaceLineEndings("\n"), actual.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task The_listing_is_stable_across_connections_and_accounts()
    {
        Assert.Equal(await BuildAsync(), await BuildAsync());
    }
}
