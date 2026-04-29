using System.Text.Json;
using System.Text.Json.Serialization;
using NexCode.Service.Lsp;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools.Implementations;

/// <summary>
/// Spec §21 LSP bridge — <c>lsp_hover</c>. Returns the language server's hover text for
/// the (file, line, column). When no LSP server is available, returns
/// <c>{ "error": "lsp_unavailable" }</c> with <c>is_error=true</c>.
/// </summary>
public sealed class LspHoverTool(LspManager manager) : ITool
{
    public string Name => "lsp_hover";

    public string Description => "Query the language server for hover/quick-info at a file position.";

    public ToolPermissionRequirement PermissionRequirement => ToolPermissionRequirement.Default;

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            file = new { type = "string" },
            line = new { type = "integer", minimum = 1 },
            column = new { type = "integer", minimum = 1 },
            language = new { type = "string" }
        },
        required = new[] { "file", "line", "column" }
    });

    public async Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<LspHoverArguments>(
            context.ArgumentsJson,
            JsonSerialization.Options) ?? throw new InvalidOperationException("lsp_hover requires arguments.");

        if (string.IsNullOrWhiteSpace(args.File))
        {
            return Error(context, "missing_file", "The 'file' argument is required.");
        }

        if (!PathSafety.EnsureWithinRoot(context.ProjectRoot, args.File, out var fullPath))
        {
            return Error(context, "path_outside_root", $"File '{args.File}' resolves outside the project root.");
        }

        var response = await manager.HoverAsync(
            new LspHoverRequest(context.ProjectRoot, fullPath, args.Line, args.Column, args.Language),
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
        return new ToolOutcome("lsp_hover", context.CallId, payload, IsError: true);
    }

    private sealed record LspHoverArguments(
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("line")] int Line,
        [property: JsonPropertyName("column")] int Column,
        [property: JsonPropertyName("language")] string? Language);
}
