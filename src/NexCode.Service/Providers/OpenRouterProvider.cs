using System.Net.Http;

namespace NexCode.Service.Providers;

/// <summary>OpenAI-compatible OpenRouter chat completions adapter.</summary>
public sealed class OpenRouterProvider : OpenAiCompatibleChatProvider
{
    public const string HttpClientName = "openrouter";

    public OpenRouterProvider(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public OpenRouterProvider(IHttpClientFactory httpClientFactory, ProviderHttpRetryPolicy policy) : base(httpClientFactory, policy) { }

    public override string Key => "openrouter";
    public override string DisplayName => "OpenRouter";
    protected override string HttpClientNameValue => HttpClientName;
    protected override string DefaultBaseUrl => "https://openrouter.ai/api";
}
