using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexCode.Shared.Contracts;

namespace NexCode.Service.Plugins;

/// <summary>
/// Central event bus for plugin hooks. Other helper subsystems call <see cref="DispatchAsync"/>
/// at well-known life-cycle moments (session start, message before send, tool call, etc.) and
/// the dispatcher fans out to every enabled plugin via <see cref="PluginSandboxRunner"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PluginHookDispatcher(
    PluginManager pluginManager,
    PluginSandboxRunner sandboxRunner,
    ServiceEventHub serviceEventHub,
    ILogger<PluginHookDispatcher> logger)
{
    public static class Hooks
    {
        public const string OnSessionStart = "on_session_start";
        public const string OnMessageBeforeSend = "on_message_before_send";
        public const string OnToolCallBefore = "on_tool_call_before";
        public const string OnToolCallAfter = "on_tool_call_after";
        public const string OnCheckpoint = "on_checkpoint";
        public const string OnSessionEnd = "on_session_end";
        public const string OnMemoryWrite = "on_memory_write";
        public const string OnPlanConfirmed = "on_plan_confirmed";
        public const string OnTodoCompleted = "on_todo_completed";
    }

    private static readonly TimeSpan DefaultHookTimeout = TimeSpan.FromSeconds(15);

    public async Task DispatchAsync(
        string hook,
        object payload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hook))
        {
            return;
        }

        IReadOnlyList<(NexCode.Data.Entities.PluginEntity Entity, PluginManifest Manifest)> plugins;
        try
        {
            plugins = await pluginManager.GetEnabledAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enumerate plugins for hook {Hook}.", hook);
            return;
        }

        foreach (var (entity, manifest) in plugins)
        {
            if (Array.IndexOf(manifest.Hooks, hook) < 0)
            {
                continue;
            }

            JsonElement? response;
            try
            {
                response = await sandboxRunner
                    .InvokeHookAsync(
                        entity.Id,
                        entity.InstallPath,
                        manifest,
                        hook,
                        payload,
                        DefaultHookTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Plugin {PluginId} hook {Hook} dispatch failed.", entity.Id, hook);
                continue;
            }

            var messageJson = response.HasValue
                ? response.Value.GetRawText()
                : string.Empty;
            serviceEventHub.Publish(
                ServiceEventTypes.PluginEvent,
                new PluginEventPayload(entity.Id, hook, messageJson));
        }
    }
}
