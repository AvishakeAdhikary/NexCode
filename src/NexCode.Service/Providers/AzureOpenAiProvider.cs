using System.Net.Http;

namespace NexCode.Service.Providers;

/// <summary>
/// Azure OpenAI deployment endpoint adapter. Treats the deployment as openai-compatible,
/// using <c>api-key</c> header (instead of Bearer) and a deployment-rooted path.
/// <para>
/// Configuration:
/// <list type="bullet">
///   <item><see cref="ProviderConfiguration.BaseUrl"/> = full deployment URL up to (but not including) <c>/chat/completions</c>, e.g. <c>https://my-resource.openai.azure.com/openai/deployments/my-deploy</c>.</item>
///   <item><see cref="ProviderConfiguration.ApiKey"/> = the Azure resource key.</item>
///   <item><see cref="ProviderConfiguration.DefaultModelId"/> = optional, used as the wire-level <c>model</c> field.</item>
/// </list>
/// </para>
/// </summary>
public sealed class AzureOpenAiProvider : OpenAiCompatibleChatProvider
{
    public const string HttpClientName = "azure-openai";
    private const string ApiVersion = "2024-10-21";

    public AzureOpenAiProvider(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    public AzureOpenAiProvider(IHttpClientFactory httpClientFactory, ProviderHttpRetryPolicy policy) : base(httpClientFactory, policy) { }

    public override string Key => "azure-openai";
    public override string DisplayName => "Azure OpenAI";
    protected override string HttpClientNameValue => HttpClientName;
    protected override string DefaultBaseUrl => string.Empty;
    protected override string ChatPath => $"/chat/completions?api-version={ApiVersion}";

    protected override void ApplyAuth(HttpRequestMessage request, ProviderConfiguration configuration)
    {
        if (!string.IsNullOrEmpty(configuration.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("api-key", configuration.ApiKey);
        }
    }
}
