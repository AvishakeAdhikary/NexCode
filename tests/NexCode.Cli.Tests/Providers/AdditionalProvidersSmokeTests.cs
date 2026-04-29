using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NexCode.Service.Providers;

namespace NexCode.Cli.Tests.Providers;

/// <summary>
/// Smoke tests for the eight additional provider adapters (Slice 0016). Each test asserts
/// that the wire request body matches the expected dialect and the streaming response is
/// parsed into the correct <see cref="ProviderEvent"/> sequence. Reuses the
/// <c>ScriptedHttpMessageHandler</c> + <c>SingleClientFactory</c> pattern from
/// AnthropicMessagesProviderTests.
/// </summary>
public sealed class AdditionalProvidersSmokeTests
{
    [Fact]
    public async Task GroqProvider_OpenAiCompatibleChat_ParsesDeltasAndCompletion()
    {
        var sse = string.Join("\n",
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello \"}}]}",
            "",
            "data: {\"choices\":[{\"delta\":{\"content\":\"world\"}}]}",
            "",
            "data: {\"choices\":[{\"finish_reason\":\"stop\",\"delta\":{}}],\"usage\":{\"prompt_tokens\":3,\"completion_tokens\":2}}",
            "",
            "data: [DONE]",
            "",
            "");

        var handler = new ScriptedHandler();
        handler.Enqueue(StreamResponse(sse));
        var provider = new GroqProvider(new SingleFactory(handler), FastPolicy());

        var events = await Collect(provider.StreamTurnAsync(BuildRequest("groq", "https://api.groq.com/openai")));

        var deltas = events.OfType<TextDeltaEvent>().Select(d => d.Delta).ToArray();
        Assert.Equal(new[] { "Hello ", "world" }, deltas);
        var done = events.OfType<TurnCompletedEvent>().Single();
        Assert.Equal("stop", done.FinishReason);
        Assert.Equal(3, done.PromptTokens);
        Assert.Equal(2, done.CompletionTokens);

        // Verify wire body.
        var body = handler.LastBody!;
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("stream").GetBoolean());
        Assert.NotEmpty(doc.RootElement.GetProperty("messages").EnumerateArray().ToList());
    }

    [Fact]
    public async Task OpenRouterProvider_StreamsToolCallArguments()
    {
        var sse = string.Join("\n",
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"c1\",\"function\":{\"name\":\"search\",\"arguments\":\"{\\\"q\\\":\"}}]}}]}",
            "",
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"hi\\\"}\"}}]}}]}",
            "",
            "data: {\"choices\":[{\"finish_reason\":\"tool_calls\",\"delta\":{}}]}",
            "",
            "");
        var handler = new ScriptedHandler();
        handler.Enqueue(StreamResponse(sse));
        var provider = new OpenRouterProvider(new SingleFactory(handler), FastPolicy());

        var events = await Collect(provider.StreamTurnAsync(BuildRequest("openrouter", "https://openrouter.ai/api")));

        var toolEvent = events.OfType<ToolUseRequestedEvent>().Single();
        Assert.Equal("c1", toolEvent.CallId);
        Assert.Equal("search", toolEvent.ToolName);
        Assert.Equal("{\"q\":\"hi\"}", toolEvent.ArgumentsJson);
    }

    [Fact]
    public async Task LmStudioProvider_DefaultsLocalhostBaseUrl()
    {
        var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}\n\n";
        var handler = new ScriptedHandler();
        handler.Enqueue(StreamResponse(sse));
        var provider = new LmStudioProvider(new SingleFactory(handler), FastPolicy());

        var events = await Collect(provider.StreamTurnAsync(BuildRequest("lmstudio", baseUrl: string.Empty)));
        Assert.Single(events.OfType<TextDeltaEvent>());
        Assert.StartsWith("http://localhost", handler.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CustomOpenAiProvider_UsesConfiguredBaseUrl()
    {
        var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"x\"}}]}\n\n";
        var handler = new ScriptedHandler();
        handler.Enqueue(StreamResponse(sse));
        var provider = new CustomOpenAiProvider(new SingleFactory(handler), FastPolicy());

        await Collect(provider.StreamTurnAsync(BuildRequest("custom", "https://example.invalid")));

        Assert.Equal("https://example.invalid/v1/chat/completions", handler.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task AzureOpenAiProvider_AddsApiKeyHeaderAndApiVersionQuery()
    {
        var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"a\"}}]}\n\n";
        var handler = new ScriptedHandler();
        handler.Enqueue(StreamResponse(sse));
        var provider = new AzureOpenAiProvider(new SingleFactory(handler), FastPolicy());

        await Collect(provider.StreamTurnAsync(BuildRequest(
            "azure-openai",
            baseUrl: "https://my.openai.azure.com/openai/deployments/dep",
            apiKey: "secret-key")));

        Assert.Contains("api-version=", handler.LastUri!.AbsoluteUri);
        Assert.Equal("secret-key", handler.LastApiKeyHeader);
        Assert.Null(handler.LastBearer);
    }

    [Fact]
    public async Task OllamaProvider_ParsesNdjsonDoneFlag()
    {
        var ndjson = string.Join('\n',
            "{\"message\":{\"role\":\"assistant\",\"content\":\"ok \"},\"done\":false}",
            "{\"message\":{\"role\":\"assistant\",\"content\":\"there\"},\"done\":false}",
            "{\"done\":true,\"done_reason\":\"stop\",\"prompt_eval_count\":4,\"eval_count\":2}",
            "");
        var handler = new ScriptedHandler();
        handler.Enqueue(StreamResponse(ndjson, "application/x-ndjson"));
        var provider = new OllamaProvider(new SingleFactory(handler), FastPolicy());

        var events = await Collect(provider.StreamTurnAsync(BuildRequest("ollama", "http://localhost:11434")));

        var deltas = events.OfType<TextDeltaEvent>().Select(d => d.Delta).ToArray();
        Assert.Equal(new[] { "ok ", "there" }, deltas);
        var done = events.OfType<TurnCompletedEvent>().Single();
        Assert.Equal("stop", done.FinishReason);
        Assert.Equal(4, done.PromptTokens);
        Assert.Equal(2, done.CompletionTokens);
    }

    [Fact]
    public async Task GeminiProvider_ParsesPartsAndFunctionCall()
    {
        var sse = string.Join("\n",
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hi\"}]}}]}",
            "",
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"functionCall\":{\"name\":\"sum\",\"args\":{\"a\":1,\"b\":2}}}]},\"finishReason\":\"STOP\"}],\"usageMetadata\":{\"promptTokenCount\":5,\"candidatesTokenCount\":1}}",
            "",
            "");
        var handler = new ScriptedHandler();
        handler.Enqueue(StreamResponse(sse));
        var provider = new GeminiProvider(new SingleFactory(handler), FastPolicy());

        var events = await Collect(provider.StreamTurnAsync(BuildRequest(
            "gemini",
            baseUrl: "https://generativelanguage.googleapis.com",
            apiKey: "k")));

        Assert.Contains(events, e => e is TextDeltaEvent t && t.Delta == "Hi");
        var tool = events.OfType<ToolUseRequestedEvent>().Single();
        Assert.Equal("sum", tool.ToolName);
        var done = events.OfType<TurnCompletedEvent>().Single();
        Assert.Equal("STOP", done.FinishReason);
        Assert.Equal(5, done.PromptTokens);
    }

    [Fact]
    public async Task BedrockProvider_WithoutCreds_EmitsNotConfiguredError()
    {
        var handler = new ScriptedHandler();
        var provider = new BedrockProvider(new SingleFactory(handler));
        var events = await Collect(provider.StreamTurnAsync(BuildRequest("bedrock", baseUrl: "https://bedrock", apiKey: "")));

        var error = events.OfType<ProviderErrorEvent>().First();
        Assert.Equal("bedrock.not_configured", error.Code);
        Assert.False(error.Recoverable);
    }

    [Fact]
    public async Task BedrockProvider_WithCreds_EmitsNotImplementedSkeletonError()
    {
        var handler = new ScriptedHandler();
        var provider = new BedrockProvider(new SingleFactory(handler));
        var events = await Collect(provider.StreamTurnAsync(
            BuildRequest("bedrock", baseUrl: "https://bedrock", apiKey: "AK|SK|us-east-1")));

        var error = events.OfType<ProviderErrorEvent>().First();
        Assert.Equal("bedrock.not_implemented", error.Code);
    }

    private static ProviderTurnRequest BuildRequest(string key, string baseUrl, string apiKey = "k")
    {
        var schema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone();
        return new ProviderTurnRequest(
            Configuration: new ProviderConfiguration(key, key, baseUrl, apiKey, "model-x"),
            ModelId: "model-x",
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

    private static HttpResponseMessage StreamResponse(string body, string contentType = "text/event-stream")
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return response;
    }

    private static ProviderHttpRetryPolicy FastPolicy()
        => new(static () => DateTimeOffset.UtcNow, static (_, _) => Task.CompletedTask);

    private static async Task<List<ProviderEvent>> Collect(IAsyncEnumerable<ProviderEvent> source)
    {
        var list = new List<ProviderEvent>();
        await foreach (var item in source) list.Add(item);
        return list;
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public string? LastBody { get; private set; }
        public Uri? LastUri { get; private set; }
        public string? LastBearer { get; private set; }
        public string? LastApiKeyHeader { get; private set; }

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            if (request.Headers.Authorization is { } auth && auth.Scheme == "Bearer")
            {
                LastBearer = auth.Parameter;
            }
            if (request.Headers.TryGetValues("api-key", out var values))
            {
                LastApiKeyHeader = values.FirstOrDefault();
            }

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("ScriptedHandler exhausted.");
            }
            return _responses.Dequeue();
        }
    }

    private sealed class SingleFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
