using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexCode.Service.Permissions;
using NexCode.Service.Providers;
using NexCode.Shared.Json;

namespace NexCode.Service.Tools;

/// <summary>
/// Default <see cref="IToolRegistry"/>. Composes all DI-registered <see cref="ITool"/>
/// instances, exposes a provider-agnostic catalog via <see cref="DescribeForModel"/>,
/// and routes <see cref="InvokeAsync"/> calls through the permission gate before
/// dispatching to the matching tool.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly IPermissionGate _permissionGate;
    private readonly ILogger<ToolRegistry> _logger;
    private readonly Dictionary<string, ITool> _toolsByName;

    public ToolRegistry(
        IEnumerable<ITool> tools,
        IPermissionGate permissionGate,
        ILogger<ToolRegistry> logger)
    {
        _permissionGate = permissionGate;
        _logger = logger;

        var materialized = tools.ToArray();
        _toolsByName = materialized.ToDictionary(
            tool => tool.Name,
            tool => tool,
            StringComparer.OrdinalIgnoreCase);
        Tools = materialized;
    }

    public IReadOnlyList<ITool> Tools { get; }

    public IEnumerable<ProviderToolDescriptor> DescribeForModel()
    {
        foreach (var tool in Tools)
        {
            yield return new ProviderToolDescriptor(
                Name: tool.Name,
                Description: tool.Description,
                InputSchema: tool.InputSchema);
        }
    }

    public async Task<ToolOutcome> InvokeAsync(
        ToolInvocationContext context,
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        if (!_toolsByName.TryGetValue(toolName, out var tool))
        {
            var unknownPayload = JsonSerializer.Serialize(
                new { error = "unknown_tool", tool = toolName },
                JsonSerialization.Options);
            return new ToolOutcome(
                ToolName: toolName,
                CallId: context.CallId,
                ResultJson: unknownPayload,
                IsError: true);
        }

        if (tool.PermissionRequirement > ToolPermissionRequirement.Default
            && context.PermissionMode == PermissionMode.Default)
        {
            var decision = await _permissionGate.RequestAsync(
                sessionId: context.Session.SessionId,
                tool: tool,
                callId: context.CallId,
                argumentsPreview: argumentsJson,
                currentMode: context.PermissionMode,
                cancellationToken: cancellationToken);

            if (!decision.Allowed)
            {
                var deniedPayload = JsonSerializer.Serialize(
                    new
                    {
                        error = "permission_denied",
                        reason = decision.DenyReason ?? "User denied tool execution.",
                        origin = decision.Origin.ToString()
                    },
                    JsonSerialization.Options);
                return new ToolOutcome(
                    ToolName: tool.Name,
                    CallId: context.CallId,
                    ResultJson: deniedPayload,
                    IsError: true);
            }
        }

        var executionContext = context with { ArgumentsJson = argumentsJson };

        try
        {
            return await tool.ExecuteAsync(executionContext, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {ToolName} threw during execution (call {CallId}).", tool.Name, context.CallId);
            var errorPayload = JsonSerializer.Serialize(
                new
                {
                    error = "tool_exception",
                    type = ex.GetType().Name,
                    message = ex.Message
                },
                JsonSerialization.Options);
            return new ToolOutcome(
                ToolName: tool.Name,
                CallId: context.CallId,
                ResultJson: errorPayload,
                IsError: true);
        }
    }
}
