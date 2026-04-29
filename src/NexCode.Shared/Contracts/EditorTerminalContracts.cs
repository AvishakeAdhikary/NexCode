namespace NexCode.Shared.Contracts;

/// <summary>
/// Slice 0015 — payloads for the editor / terminal / lsp IPC bridge.
/// </summary>
public sealed record EditorOpenFileRequest(
    string ProjectPath,
    string FilePath);

public sealed record EditorOpenFileResponse(
    string ProjectPath,
    string FilePath,
    string Language,
    string Content,
    long Size,
    bool Truncated,
    string? Error);

public sealed record EditorSaveFileRequest(
    string ProjectPath,
    string FilePath,
    string Content);

public sealed record EditorSaveFileResponse(
    string ProjectPath,
    string FilePath,
    bool Success,
    string? Error);

public sealed record TerminalSpawnRequest(
    string ProjectPath,
    string? Shell,
    string? WorkingDirectory,
    int Cols,
    int Rows,
    string[]? EnvVars);

public sealed record TerminalSpawnResponse(
    string TerminalId,
    string Shell,
    string Executable,
    string WorkingDirectory,
    bool Started,
    string? Error);

public sealed record TerminalWriteRequest(
    string TerminalId,
    string DataBase64,
    int? Cols,
    int? Rows);

public sealed record TerminalWriteResponse(
    string TerminalId,
    bool Accepted,
    string? Error);

public sealed record TerminalKillRequest(string TerminalId);

public sealed record TerminalKillResponse(
    string TerminalId,
    bool Killed,
    string? Error);

public sealed record TerminalOutputEventPayload(
    string TerminalId,
    string DataBase64);

public sealed record TerminalExitEventPayload(
    string TerminalId,
    int ExitCode);

public sealed record LinterMarker(
    string FilePath,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Severity,
    string Message,
    string? Source);

public sealed record LinterDiagnosticEventPayload(
    string FilePath,
    string Language,
    LinterMarker[] Markers);

public sealed record LspHoverRequest(
    string ProjectPath,
    string FilePath,
    int Line,
    int Column,
    string? Language);

public sealed record LspHoverResponse(
    string FilePath,
    string? Contents,
    int Line,
    int Column,
    bool Available,
    string? Error);

public sealed record LspDiagnosticsRequest(
    string ProjectPath,
    string FilePath,
    string? Language);

public sealed record LspDiagnosticsResponse(
    string FilePath,
    LinterMarker[] Markers,
    bool Available,
    string? Error);

public sealed record FileChangedEventPayload(
    string FilePath,
    string ChangeKind);
