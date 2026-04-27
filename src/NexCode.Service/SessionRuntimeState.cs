using NexCode.Shared.Contracts;

namespace NexCode.Service;

public sealed record SessionRuntimeState(
    Guid SessionId,
    SessionCreateRequest Request,
    DateTimeOffset CreatedAt);
