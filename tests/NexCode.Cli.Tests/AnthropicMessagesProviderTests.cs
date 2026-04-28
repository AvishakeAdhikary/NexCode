using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NexCode.Service.Providers;

namespace NexCode.Cli.Tests;

public sealed class AnthropicMessagesProviderTests
{
    private static ProviderTurnRequest BuildRequest()
    {
        var schema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone();
        return new ProviderTurnRequest(
            Configuration: new ProviderConfiguration(
                ProviderKey: "anthropic",
                DisplayName: "Anthropic Claude",
                BaseUrl: "https://api.anthropic.test",
                ApiKey: "key-abc",
                DefaultModelId: "claude-x"),
            ModelId: "claude-x",
            SystemPrompt: "you are helpful",
            Conversation: new List<ProviderConversationMessage>
            {
                new(ProviderMessageRole.User, "hello"),
            },
            Tools: new List<ProviderToolDescriptor>
            {
                new("ping", "ping tool", schema),
            },
            MaxOutputTokens: 64,
            Temperature: 0.5);
    }

    [Fact]
    public async Task TextOnlyStream_EmitsThreeDeltasThenCompletion()
    {
        var sse = string.Join("\n",
            "event: message_start",
            "data: {\"type\":\"message_start\",\"message\":{\"id\":\"m1\",\"usage\":{\"input_tokens\":7}}}",
            "",
            "event: content_block_start",
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}",
            "",
            "event: content_block_delta",
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello\"}}",
            "",
            "event: content_block_delta",
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\", \"}}",
            "",
            "event: content_block_delta",
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"world\"}}",
            "",
            "event: content_block_stop",
            "data: {\"type\":\"content_block_stop\",\"index\":0}",
            "",
            "event: message_delta",
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"input_tokens\":7,\"output_tokens\":3}}",
            "",
            "event: message_stop",
            "data: {\"type\":\"message_stop\"}",
            "",
            "");

        var handler = new ScriptedHttpMessageHandler();
        handler.Enqueue(StreamResponse(HttpStatusCode.OK, sse));
        var provider = new AnthropicMessagesProvider(
            new SingleClientFactory(handler),
            FastRetryPolicy());

        var events = await CollectAsync(provider.StreamTurnAsync(BuildRequest()));

        var deltas = events.OfType<TextDeltaEvent>().ToList();
        Assert.Equal(3, deltas.Count);
        Assert.Equal("Hello", deltas[0].Delta);
        Assert.Equal(", ", deltas[1].Delta);
        Assert.Equal("world", deltas[2].Delta);

        var completion = events.OfType<TurnCompletedEvent>().Single();
        Assert.Equal("end_turn", completion.FinishReason);
        Assert.Equal(7, completion.PromptTokens);
        Assert.Equal(3, completion.CompletionTokens);
    }

    [Fact]
    public async Task ToolUseStream_AccumulatesArgumentsAndEmitsToolEvent()
    {
        var sse = string.Join("\n",
            "event: message_start",
            "data: {\"type\":\"message_start\",\"message\":{\"id\":\"m2\",\"usage\":{\"input_tokens\":4}}}",
            "",
            "event: content_block_start",
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"call_42\",\"name\":\"search\"}}",
            "",
            "event: content_block_delta",
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"q\\\":\"}}",
            "",
            "event: content_block_delta",
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"\\\"hello\\\"}\"}}",
            "",
            "event: content_block_stop",
            "data: {\"type\":\"content_block_stop\",\"index\":0}",
            "",
            "event: message_delta",
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"},\"usage\":{\"input_tokens\":4,\"output_tokens\":12}}",
            "",
            "event: message_stop",
            "data: {\"type\":\"message_stop\"}",
            "",
            "");

        var handler = new ScriptedHttpMessageHandler();
        handler.Enqueue(StreamResponse(HttpStatusCode.OK, sse));
        var provider = new AnthropicMessagesProvider(
            new SingleClientFactory(handler),
            FastRetryPolicy());

        var events = await CollectAsync(provider.StreamTurnAsync(BuildRequest()));

        var toolEvent = events.OfType<ToolUseRequestedEvent>().Single();
        Assert.Equal("call_42", toolEvent.CallId);
        Assert.Equal("search", toolEvent.ToolName);
        Assert.Equal("{\"q\":\"hello\"}", toolEvent.ArgumentsJson);

        Assert.Single(events.OfType<TurnCompletedEvent>());
    }

    [Fact]
    public async Task RateLimited_ThenSucceeds_EmitsExactlyOneRetryEvent()
    {
        var successSse = string.Join("\n",
            "event: message_start",
            "data: {\"type\":\"message_start\",\"message\":{\"id\":\"m3\",\"usage\":{\"input_tokens\":1}}}",
            "",
            "event: content_block_start",
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}",
            "",
            "event: content_block_delta",
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"ok\"}}",
            "",
            "event: content_block_stop",
            "data: {\"type\":\"content_block_stop\",\"index\":0}",
            "",
            "event: message_delta",
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}",
            "",
            "event: message_stop",
            "data: {\"type\":\"message_stop\"}",
            "",
            "");

        var handler = new ScriptedHttpMessageHandler();
        var rateLimited = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("rate limited"),
        };
        rateLimited.Headers.TryAddWithoutValidation("Retry-After", "1");
        handler.Enqueue(rateLimited);
        handler.Enqueue(StreamResponse(HttpStatusCode.OK, successSse));

        var provider = new AnthropicMessagesProvider(
            new SingleClientFactory(handler),
            FastRetryPolicy());

        var events = await CollectAsync(provider.StreamTurnAsync(BuildRequest()));

        var retries = events.OfType<ProviderRetryEvent>().ToList();
        Assert.Single(retries);
        Assert.Equal(1, retries[0].Attempt);
        Assert.Single(events.OfType<TurnCompletedEvent>());
        Assert.Empty(events.OfType<ProviderErrorEvent>());
        Assert.Equal(2, handler.Sent);
    }

    [Fact]
    public async Task PersistentServerError_RetriesThreeTimesThenYieldsErrorEvent()
    {
        var handler = new ScriptedHttpMessageHandler();
        for (var i = 0; i < 3; i++)
        {
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom"),
            });
        }

        var provider = new AnthropicMessagesProvider(
            new SingleClientFactory(handler),
            FastRetryPolicy());

        var events = await CollectAsync(provider.StreamTurnAsync(BuildRequest()));

        Assert.Equal(2, events.OfType<ProviderRetryEvent>().Count());
        var error = events.OfType<ProviderErrorEvent>().Single();
        Assert.False(error.Recoverable);
        Assert.Equal("anthropic.retries_exhausted", error.Code);
        Assert.Equal(3, handler.Sent);
    }

    private static HttpResponseMessage StreamResponse(HttpStatusCode status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var response = new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(bytes),
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        return response;
    }

    private static ProviderHttpRetryPolicy FastRetryPolicy()
        => new(static () => DateTimeOffset.UtcNow, static (_, _) => Task.CompletedTask);

    private static async Task<List<ProviderEvent>> CollectAsync(IAsyncEnumerable<ProviderEvent> source)
    {
        var list = new List<ProviderEvent>();
        await foreach (var item in source)
        {
            list.Add(item);
        }
        return list;
    }
}

internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    public int Sent { get; private set; }

    public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sent++;
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("ScriptedHttpMessageHandler exhausted.");
        }
        return Task.FromResult(_responses.Dequeue());
    }
}

internal sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
