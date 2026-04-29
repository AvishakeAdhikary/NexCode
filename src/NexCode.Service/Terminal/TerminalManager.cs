using System;
using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexCode.Shared.Contracts;

namespace NexCode.Service.Terminal;

/// <summary>
/// Lifecycle owner for active ConPTY sessions. Each spawn is keyed by a Guid terminalId
/// returned to the GUI. Output bytes are forwarded as base64 strings on the
/// <see cref="ServiceEventHub"/> using the <see cref="ServiceEventTypes.TerminalOutput"/>
/// envelope; exit codes use <see cref="ServiceEventTypes.TerminalExit"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TerminalManager(ILogger<TerminalManager> logger, ServiceEventHub eventHub)
{
    private readonly ConcurrentDictionary<Guid, TerminalSession> _sessions = new();

    public TerminalSpawnResponse Spawn(TerminalSpawnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolved = ShellResolver.Resolve(request.Shell);
        if (!resolved.Available || string.IsNullOrWhiteSpace(resolved.Executable))
        {
            return new TerminalSpawnResponse(
                TerminalId: string.Empty,
                Shell: request.Shell ?? "pwsh",
                Executable: resolved.Executable ?? string.Empty,
                WorkingDirectory: request.WorkingDirectory ?? string.Empty,
                Started: false,
                Error: $"Shell '{request.Shell ?? "pwsh"}' not found on host.");
        }

        var workingDir = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? request.ProjectPath
            : request.WorkingDirectory!;

        var pty = new ConPtyHost();
        var terminalId = Guid.NewGuid();
        var idString = terminalId.ToString("N");

        pty.OutputReceived += (_, bytes) =>
        {
            try
            {
                eventHub.Publish(
                    ServiceEventTypes.TerminalOutput,
                    new TerminalOutputEventPayload(idString, Convert.ToBase64String(bytes)));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to publish terminal output for {TerminalId}", idString);
            }
        };

        pty.Exited += (_, exitCode) =>
        {
            try
            {
                eventHub.Publish(
                    ServiceEventTypes.TerminalExit,
                    new TerminalExitEventPayload(idString, exitCode));
            }
            catch
            {
                // best-effort
            }

            if (_sessions.TryRemove(terminalId, out var removed))
            {
                _ = removed.DisposeAsync();
            }
        };

        var size = new ConPtySize(
            Cols: request.Cols > 0 ? request.Cols : 120,
            Rows: request.Rows > 0 ? request.Rows : 30);

        try
        {
            pty.StartAsync(
                resolved.Executable,
                resolved.Args,
                workingDir,
                request.EnvVars ?? Array.Empty<string>(),
                size,
                CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start ConPTY session.");
            pty.Dispose();
            return new TerminalSpawnResponse(
                TerminalId: string.Empty,
                Shell: resolved.Logical,
                Executable: resolved.Executable,
                WorkingDirectory: workingDir,
                Started: false,
                Error: ex.Message);
        }

        var session = new TerminalSession(terminalId, resolved, pty);
        _sessions[terminalId] = session;

        return new TerminalSpawnResponse(
            TerminalId: idString,
            Shell: resolved.Logical,
            Executable: resolved.Executable,
            WorkingDirectory: workingDir,
            Started: true,
            Error: null);
    }

    public async Task<TerminalWriteResponse> WriteAsync(TerminalWriteRequest request)
    {
        if (!Guid.TryParseExact(request.TerminalId, "N", out var id) && !Guid.TryParse(request.TerminalId, out id))
        {
            return new TerminalWriteResponse(request.TerminalId, false, "invalid_terminal_id");
        }

        if (!_sessions.TryGetValue(id, out var session))
        {
            return new TerminalWriteResponse(request.TerminalId, false, "terminal_not_found");
        }

        if (!string.IsNullOrEmpty(request.DataBase64))
        {
            byte[] data;
            try
            {
                data = Convert.FromBase64String(request.DataBase64);
            }
            catch (FormatException)
            {
                return new TerminalWriteResponse(request.TerminalId, false, "invalid_base64");
            }

            await session.Pty.WriteAsync(data);
        }

        if (request.Cols is int cols && request.Rows is int rows)
        {
            await session.Pty.ResizeAsync(cols, rows);
        }

        return new TerminalWriteResponse(request.TerminalId, true, null);
    }

    public async Task<TerminalKillResponse> KillAsync(TerminalKillRequest request)
    {
        if (!Guid.TryParseExact(request.TerminalId, "N", out var id) && !Guid.TryParse(request.TerminalId, out id))
        {
            return new TerminalKillResponse(request.TerminalId, false, "invalid_terminal_id");
        }

        if (!_sessions.TryRemove(id, out var session))
        {
            return new TerminalKillResponse(request.TerminalId, false, "terminal_not_found");
        }

        session.Pty.Kill();
        await session.DisposeAsync();
        return new TerminalKillResponse(request.TerminalId, true, null);
    }

    public int ActiveCount => _sessions.Count;

    private sealed class TerminalSession(Guid id, ResolvedShell shell, ConPtyHost pty) : IAsyncDisposable
    {
        public Guid Id { get; } = id;
        public ResolvedShell Shell { get; } = shell;
        public ConPtyHost Pty { get; } = pty;

        public ValueTask DisposeAsync() => Pty.DisposeAsync();
    }
}
