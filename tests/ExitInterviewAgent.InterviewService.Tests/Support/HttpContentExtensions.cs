using System.Text.Json;

namespace ExitInterviewAgent.InterviewService.Tests.Support;

public static class HttpContentExtensions
{
    public static async Task<JsonElement> ReadFromJsonAsyncElement(this HttpContent content)
        => JsonDocument.Parse(await content.ReadAsStringAsync()).RootElement.Clone();
}

public static class TokenBuilderExtensions
{
    public static TokenBuilder With(this TokenBuilder builder, Action<TokenBuilder> change)
    {
        change(builder);
        return builder;
    }
}
