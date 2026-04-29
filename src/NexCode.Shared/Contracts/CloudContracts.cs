using System;

namespace NexCode.Shared.Contracts;

/// <summary>
/// Spec §23 — payload returned by the IPC <c>cloud.status</c> handler so the GUI
/// can render the cloud connection chip in the status bar.
/// </summary>
public sealed record CloudStatusResponse(
    bool Connected,
    string? Endpoint,
    DateTimeOffset? LastSyncAt);

public sealed record RemoteStatusResponse(
    bool Connected,
    string? Endpoint,
    string? Version,
    string? State);

public sealed record RemoteConnectRequest(
    string? Endpoint,
    string? BearerToken);

public sealed record RemoteConnectResponse(
    bool Connected,
    string? Endpoint,
    string? Message);

public sealed record BackgroundServiceModeRequest(bool Enabled);

public sealed record BackgroundServiceModeResponse(bool Enabled, bool StartupRegistered);
