using System.Text;
using ExitInterviewAgent.InterviewService.Mcp;

namespace ExitInterviewAgent.InterviewService.Tests.Mcp;

public sealed class McpBodyScrubberTests
{
    private static string Scrub(string json) => Encoding.UTF8.GetString(McpBodyScrubber.Scrub(Encoding.UTF8.GetBytes(json)));

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"submit_interview_record","arguments":{"record":{}}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"prompts/get","params":{"name":"conduct_exit_interview","arguments":{"language":"pl"}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"resources/read","params":{"uri":"exit-interview://schema/record/v1"}}""")]
    [InlineData("""{"jsonrpc":"2.0","method":"notifications/initialized"}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","clientInfo":{"name":"x","version":"1"}}}""")]
    public void Known_requests_are_returned_untouched(string json)
        => Assert.Equal(json, Scrub(json));

    [Fact]
    public void Known_requests_return_the_very_same_array()
    {
        var body = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");

        Assert.Same(body, McpBodyScrubber.Scrub(body));
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"zz","params":{}}""", """{"jsonrpc":"2.0","id":1,"method":"unknown/method","params":{}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"zz","arguments":{}}}""", """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"unknown","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"prompts/get","params":{"name":"zz"}}""", """{"jsonrpc":"2.0","id":1,"method":"prompts/get","params":{"name":"unknown"}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"resources/read","params":{"uri":"exit-interview://zz"}}""", """{"jsonrpc":"2.0","id":1,"method":"resources/read","params":{"uri":"exit-interview://unknown"}}""")]
    [InlineData("""{"params":{"uri":"zz","name":"zz"},"method":"x","id":3}""", """{"params":{"uri":"exit-interview://unknown","name":"unknown"},"method":"unknown/method","id":3}""")] // any property order
    [InlineData("""{"method":"tools/call","method":"zz","params":{"name":"submit_interview_record","name":"zz"}}""", """{"method":"tools/call","method":"unknown/method","params":{"name":"submit_interview_record","name":"unknown"}}""")] // duplicates: each occurrence
    [InlineData("""{"method":"tools/call","params":{"name":"zz"}}""", """{"method":"tools/call","params":{"name":"unknown"}}""")] // escapes are compared after unescaping
    public void Unknown_identifiers_become_constants_and_nothing_else_changes(string input, string expected)
        => Assert.Equal(expected, Scrub(input));

    [Fact]
    public void The_record_and_other_arguments_are_preserved_byte_for_byte_including_duplicate_keys()
    {
        const string input = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"zz","arguments":{"record":{"a":1,"a":2,"name":"zz","uri":"zz","method":"zz"},"name":"zz"}}}""";
        const string expected = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"unknown","arguments":{"record":{"a":1,"a":2,"name":"zz","uri":"zz","method":"zz"},"name":"zz"}}}""";

        Assert.Equal(expected, Scrub(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json zzcanary")]
    [InlineData("""{"method": "zz", """)]
    [InlineData("""[{"method":"zz"}]""")]
    public void A_body_that_is_not_a_well_formed_object_is_returned_unchanged_for_the_sdk_to_refuse(string input)
        => Assert.Equal(input, Scrub(input));

    [Fact]
    public void Non_ascii_and_quote_characters_in_the_replaced_text_cannot_break_the_json()
    {
        var output = Scrub("""{"method":"tools/call","params":{"name":"zz\"\\ \u0000 ż\n"}}""");

        Assert.Equal("""{"method":"tools/call","params":{"name":"unknown"}}""", output);
    }
}
