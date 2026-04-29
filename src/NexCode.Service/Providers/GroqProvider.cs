using System.Net.Http;

namespace NexCode.Service.Providers;

/// <summary>OpenAI-compatible Groq Cloud chat completions adapter.</summary>
public sealed class GroqProvider : OpenAiCompatibleChatProvider
{
    public const string HttpClientName = "groq";

    public GroqProvider(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public GroqProvider(IHttpClientFactory httpClientFactory, ProviderHttpRetryPolicy policy) : base(httpClientFactory, policy) { }

    public override string Key => "groq";
    public override string DisplayName => "Groq";
    protected override string HttpClientNameValue => HttpClientName;
    protected override string DefaultBaseUrl => "https://api.groq.com/openai";
}
