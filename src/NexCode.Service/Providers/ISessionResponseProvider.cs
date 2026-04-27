namespace NexCode.Service.Providers;

public interface ISessionResponseProvider
{
    IAsyncEnumerable<ProviderTurnUpdate> GenerateTurnAsync(
        SessionRuntimeState session,
        string userContent,
        CancellationToken cancellationToken = default);
}

public abstract record ProviderTurnUpdate;

public sealed record ProviderStatusUpdate(
    string Message,
    string Level) : ProviderTurnUpdate;

public sealed record ProviderToolCallUpdate(
    string ToolName,
    string ArgumentsJson,
    string CallId) : ProviderTurnUpdate;

public sealed record ProviderToolResultUpdate(
    string CallId,
    string ResultJson,
    bool IsError) : ProviderTurnUpdate;

public sealed record ProviderTokenUpdate(
    string Content) : ProviderTurnUpdate;
