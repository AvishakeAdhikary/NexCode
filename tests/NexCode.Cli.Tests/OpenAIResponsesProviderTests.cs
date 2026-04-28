using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NexCode.Service.Providers;

namespace NexCode.Cli.Tests;

public sealed class OpenAIResponsesProviderTests
{
    private static ProviderTurnRequest BuildRequest()
    {
        var schema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone();
        return new ProviderTurnRequest(
            Configuration: new ProviderConfiguration(
                ProviderKey: "openai",
                DisplayName: "OpenAI",
                BaseUrl: "https://api.openai.test",
                ApiKey: "sk-test",
                DefaultModelId: "gpt-x"),
            ModelId: "gpt-x",
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
            "event: response.output_text.delta",
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\"Hello\"}",
            "",
            "event: response.output_text.delta",
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\", \"}",
            "",
            "event: response.output_text.delta",
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\"world\"}",
            "",
            "event: response.completed",
            "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"usage\":{\"input_tokens\":11,\"output_tokens\":3}}}",
            "",
            "");

        var handler = new ScriptedHttpMessageHandler();
        handler.Enqueue(StreamResponse(HttpStatusCode.OK, sse));
        var provider = new OpenAIResponsesProvider(
            new SingleClientFactory(handler),
            FastRetryPolicy());

        var events = await CollectAsync(provider.StreamTurnAsync(BuildRequest()));

        var deltas = events.OfType<TextDeltaEvent>().ToList();
        Assert.Equal(3, deltas.Count);
        Assert.Equal("Hello", deltas[0].Delta);
        Assert.Equal(", ", deltas[1].Delta);
        Assert.Equal("world", deltas[2].Delta);

        var completion = events.OfType<TurnCompletedEvent>().Single();
        Assert.Equal("completed", completion.FinishReason);
        Assert.Equal(11, completion.PromptTokens);
        Assert.Equal(3, completion.CompletionTokens);
    }

    [Fact]
    public async Task ToolUseStream_AccumulatesArgumentsAndEmitsToolEvent()
    {
        var sse = string.Join("\n",
            "event: response.output_item.added",
            "data: {\"type\":\"response.output_item.added\",\"item\":{\"id\":\"item_1\",\"type\":\"function_call\",\"call_id\":\"call_99\",\"name\":\"search\"}}",
            "",
            "event: response.function_call_arguments.delta",
            "data: {\"type\":\"response.function_call_arguments.delta\",\"item_id\":\"item_1\",\"delta\":\"{\\\"q\\\":\"}",
            "",
            "event: response.function_call_arguments.delta",
            "data: {\"type\":\"response.function_call_arguments.delta\",\"item_id\":\"item_1\",\"delta\":\"\\\"hello\\\"}\"}",
            "",
            "event: response.output_item.done",
            "data: {\"type\":\"response.output_item.done\",\"item\":{\"id\":\"item_1\",\"type\":\"function_call\",\"call_id\":\"call_99\",\"name\":\"search\"}}",
            "",
            "event: response.completed",
            "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"usage\":{\"input_tokens\":4,\"output_tokens\":12}}}",
            "",
            "");

        var handler = new ScriptedHttpMessageHandler();
        handler.Enqueue(StreamResponse(HttpStatusCode.OK, sse));
        var provider = new OpenAIResponsesProvider(
            new SingleClientFactory(handler),
            FastRetryPolicy());

        var events = await CollectAsync(provider.StreamTurnAsync(BuildRequest()));

        var toolEvent = events.OfType<ToolUseRequestedEvent>().Single();
        Assert.Equal("call_99", toolEvent.CallId);
        Assert.Equal("search", toolEvent.ToolName);
        Assert.Equal("{\"q\":\"hello\"}", toolEvent.ArgumentsJson);

        Assert.Single(events.OfType<TurnCompletedEvent>());
    }

    [Fact]
    public async Task RateLimited_ThenSucceeds_EmitsExactlyOneRetryEvent()
    {
        var successSse = string.Join("\n",
            "event: response.output_text.delta",
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\"ok\"}",
            "",
            "event: response.completed",
            "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}}",
            "",
            "");

        var handler = new ScriptedHttpMessageHandler();
        var rateLimited = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("slow down"),
        };
        rateLimited.Headers.TryAddWithoutValidation("Retry-After", "1");
        handler.Enqueue(rateLimited);
        handler.Enqueue(StreamResponse(HttpStatusCode.OK, successSse));

        var provider = new OpenAIResponsesProvider(
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
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("upstream"),
            });
        }

        var provider = new OpenAIResponsesProvider(
            new SingleClientFactory(handler),
            FastRetryPolicy());

        var events = await CollectAsync(provider.StreamTurnAsync(BuildRequest()));

        Assert.Equal(2, events.OfType<ProviderRetryEvent>().Count());
        var error = events.OfType<ProviderErrorEvent>().Single();
        Assert.False(error.Recoverable);
        Assert.Equal("openai.retries_exhausted", error.Code);
        Assert.Equal(3, handler.Sent);
    }

    [Fact]
    public void Registry_ResolvesProvidersByKey()
    {
        var anthropic = new AnthropicMessagesProvider(new SingleClientFactory(new ScriptedHttpMessageHandler()));
        var openai = new OpenAIResponsesProvider(new SingleClientFactory(new ScriptedHttpMessageHandler()));
        var registry = new ModelProviderRegistry(new IModelProvider[] { anthropic, openai });

        Assert.Equal(2, registry.All.Count);
        Assert.Same(anthropic, registry.FindByKey("anthropic"));
        Assert.Same(openai, registry.FindByKey("openai"));
        Assert.Same(anthropic, registry.FindByKey("ANTHROPIC"));
        Assert.Null(registry.FindByKey("unknown"));
        Assert.Null(registry.FindByKey(""));
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
