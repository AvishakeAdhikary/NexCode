using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Lsp;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §21 LSP bridge — <c>lsp_diagnostics</c>. Returns the latest set of LSP markers
/// for a file. Until the streaming pipeline lands, this returns an empty array when the
/// server is healthy and <c>{"error":"lsp_unavailable"}</c> otherwise.
/// </summary>
public sealed class LspDiagnosticsTool(LspManager manager) : ITool
{
    public string Name => "lsp_diagnostics";

    public string Description => "Return the latest language-server diagnostics for a file.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            file = new { type = "string" },
            language = new { type = "string" }
        },
        required = new[] { "file" }
    });

    public async Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<LspDiagnosticsArguments>(
            context.ArgumentsJson,
            JsonSerialization.Options) ?? throw new InvalidOperationException("lsp_diagnostics requires arguments.");

        if (string.IsNullOrWhiteSpace(args.File))
        {
            return Error(context, "missing_file", "The 'file' argument is required.");
        }

        if (!PathSafety.EnsureWithinRoot(context.ProjectRoot, args.File, out var fullPath))
        {
            return Error(context, "path_outside_root", $"File '{args.File}' resolves outside the project root.");
        }

        var response = await manager.DiagnosticsAsync(
            new LspDiagnosticsRequest(context.ProjectRoot, fullPath, args.Language),
            cancellationToken);

        if (!response.Available)
        {
            return Error(context, "lsp_unavailable", response.Error ?? "No language server is available for this file.");
        }

        var payload = JsonSerializer.Serialize(response, JsonSerialization.Options);
        return new ToolOutcome(Name, context.CallId, payload, IsError: false);
    }

    private static ToolOutcome Error(ToolInvocationContext context, string code, string message)
    {
        var payload = JsonSerializer.Serialize(new { error = code, message }, JsonSerialization.Options);
        return new ToolOutcome("lsp_diagnostics", context.CallId, payload, IsError: true);
    }

    private sealed record LspDiagnosticsArguments(
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("language")] string? Language);
}
