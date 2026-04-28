using System.Text.Json;
using NexCode.Service.Permissions;
using NexCode.Service.Providers;

namespace NexCode.Service.Tools;

/// <summary>
/// One executable built-in tool from spec §5.4. Each tool owns its own argument schema,
/// permission requirement, and execution body. The <see cref="IToolRegistry"/> composes
/// many <see cref="ITool"/>s and exposes them to the model via
/// <see cref="ProviderToolDescriptor"/>.
/// </summary>
public interface ITool
{
    /// <summary>Spec-stable lowercase tool name (e.g. <c>read_file</c>).</summary>
    string Name { get; }

    /// <summary>Short description shown to the model in the tool catalog.</summary>
    string Description { get; }

    /// <summary>
    /// JSON Schema for the tool's input arguments. Providers translate this to their
    /// native function-calling format.
    /// </summary>
    JsonElement InputSchema { get; }

    /// <summary>
    /// Permission gate level required to invoke this tool. Tools whose
    /// <see cref="PermissionRequirement"/> is <see cref="ToolPermissionRequirement.Default"/>
    /// run without confirmation in Default Access. Tools marked
    /// <see cref="ToolPermissionRequirement.Warned"/> raise a permission_request unless the
    /// session is in Full Access. Tools marked <see cref="ToolPermissionRequirement.Full"/>
    /// always require explicit consent unless pre-granted for the session.
    /// </summary>
    ToolPermissionRequirement PermissionRequirement { get; }

    /// <summary>Run the tool. Implementations must respect cancellation and sandbox flags.</summary>
    Task<ToolOutcome> ExecuteAsync(ToolInvocationContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Routing layer between the agent loop and the concrete <see cref="ITool"/> set.
/// Centralizes argument validation, permission gating, and error wrapping.
/// </summary>
public interface IToolRegistry
{
    IReadOnlyList<ITool> Tools { get; }

    IEnumerable<ProviderToolDescriptor> DescribeForModel();

    Task<ToolOutcome> InvokeAsync(
        ToolInvocationContext context,
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken);
}

/// <summary>Inputs available to every tool invocation.</summary>
public sealed record ToolInvocationContext(
    SessionRuntimeState Session,
    string ProjectRoot,
    bool SandboxEnabled,
    PermissionMode PermissionMode,
    string CallId,
    string ArgumentsJson = "{}");

/// <summary>Result of running a single tool. <see cref="ResultJson"/> is fed back to the model verbatim.</summary>
public sealed record ToolOutcome(
    string ToolName,
    string CallId,
    string ResultJson,
    bool IsError,
    string[]? FilesChanged = null);

public enum ToolPermissionRequirement { Default, Warned, Full }
