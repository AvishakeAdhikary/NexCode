using System.Net.Http;

namespace NexCode.Service.Providers;

/// <summary>
/// LM Studio local OpenAI-compatible server adapter. Auth is typically a no-op for local
/// installs but a Bearer token is honoured when supplied for tunnelled deployments.
/// </summary>
public sealed class LmStudioProvider : OpenAiCompatibleChatProvider
{
    public const string HttpClientName = "lmstudio";

    public LmStudioProvider(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public LmStudioProvider(IHttpClientFactory httpClientFactory, ProviderHttpRetryPolicy policy) : base(httpClientFactory, policy) { }

    public override string Key => "lmstudio";
    public override string DisplayName => "LM Studio (local)";
    protected override string HttpClientNameValue => HttpClientName;
    protected override string DefaultBaseUrl => "http://localhost:1234";
}
