using System.Net.Http;

namespace NexCode.Service.Providers;

/// <summary>
/// Generic OpenAI-compatible HTTP adapter for self-hosted / private endpoints. The user
/// configures <see cref="ProviderConfiguration.BaseUrl"/> directly; falls back to the
/// canonical <c>https://api.openai.com</c> for convenience.
/// </summary>
public sealed class CustomOpenAiProvider : OpenAiCompatibleChatProvider
{
    public const string HttpClientName = "custom";

    public CustomOpenAiProvider(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public CustomOpenAiProvider(IHttpClientFactory httpClientFactory, ProviderHttpRetryPolicy policy) : base(httpClientFactory, policy) { }

    public override string Key => "custom";
    public override string DisplayName => "Custom (OpenAI-compatible)";
    protected override string HttpClientNameValue => HttpClientName;
    protected override string DefaultBaseUrl => "https://api.openai.com";
}
