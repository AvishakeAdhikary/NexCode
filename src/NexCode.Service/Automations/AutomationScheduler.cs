using System.Collections.Concurrent;
using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexCode.Data.Entities;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Automations;

/// <summary>
/// Hosted background service driving spec §22.2 trigger sources: cron, file change,
/// git events (via <see cref="ServiceEventHub"/> tap), session end, and webhook.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AutomationScheduler(
    AutomationEngine engine,
    ServiceEventHub serviceEventHub,
    ILogger<AutomationScheduler> logger) : BackgroundService
{
    public const int DefaultWebhookPort = 51723;

    private readonly ConcurrentDictionary<Guid, FileSystemWatcher> _fileWatchers = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _nextCronFire = new();
    private readonly ConcurrentDictionary<string, Guid> _webhookRoutes = new(StringComparer.OrdinalIgnoreCase);
    private long _lastSeenSequence;
    private HttpListener? _httpListener;

    public static int WebhookPort =>
        int.TryParse(Environment.GetEnvironmentVariable("NEXCODE_AUTOMATION_PORT"), out var port) && port > 0
            ? port
            : DefaultWebhookPort;

    public DateTime? GetNextCronFireUtc(Guid automationId) =>
        _nextCronFire.TryGetValue(automationId, out var t) ? t : null;

    /// <summary>Static helper exposed for unit tests that don't need a hosted-service instance.</summary>
    public static DateTimeOffset? ComputeNextFire(string cron, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            return null;
        }

        var format = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length switch
        {
            6 => CronFormat.IncludeSeconds,
            _ => CronFormat.Standard
        };

        var expression = CronExpression.Parse(cron, format);
        return expression.GetNextOccurrence(nowUtc, TimeZoneInfo.Utc);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AutomationScheduler started.");
        await ReloadAsync(stoppingToken).ConfigureAwait(false);
        StartWebhookListener(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AutomationScheduler tick failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        StopWebhookListener();
        DisposeAllWatchers();
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var rows = await engine.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        _webhookRoutes.Clear();
        foreach (var row in rows)
        {
            engine.RegisterScheduled(row);
        }

        foreach (var sched in engine.ActiveSchedules)
        {
            ApplyTrigger(sched);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        DrainEventsForGitTriggers();

        foreach (var sched in engine.ActiveSchedules)
        {
            if (sched.Trigger is not ScheduleTrigger schedule)
            {
                continue;
            }

            if (!_nextCronFire.TryGetValue(sched.Entity.Id, out var nextFire))
            {
                var first = ComputeNextFire(schedule.Cron, now);
                if (first is null)
                {
                    continue;
                }

                _nextCronFire[sched.Entity.Id] = first.Value.UtcDateTime;
                continue;
            }

            if (now < nextFire)
            {
                continue;
            }

            await engine.ExecuteAsync(sched.Entity, "schedule", cancellationToken).ConfigureAwait(false);
            var next = ComputeNextFire(schedule.Cron, now);
            if (next is not null)
            {
                _nextCronFire[sched.Entity.Id] = next.Value.UtcDateTime;
            }
            else
            {
                _nextCronFire.TryRemove(sched.Entity.Id, out _);
            }
        }
    }

    private void ApplyTrigger(ScheduledAutomation scheduled)
    {
        switch (scheduled.Trigger)
        {
            case ScheduleTrigger schedule:
                var first = ComputeNextFire(schedule.Cron, DateTime.UtcNow);
                if (first is not null)
                {
                    _nextCronFire[scheduled.Entity.Id] = first.Value.UtcDateTime;
                }
                break;
            case FileChangeTrigger fileChange when Directory.Exists(fileChange.ProjectPath):
                StartFileWatcher(scheduled.Entity, fileChange);
                break;
            case WebhookTrigger webhook:
                _webhookRoutes[webhook.Path] = scheduled.Entity.Id;
                break;
        }
    }

    private void StartFileWatcher(AutomationEntity entity, FileChangeTrigger trigger)
    {
        if (_fileWatchers.ContainsKey(entity.Id))
        {
            return;
        }

        var watcher = new FileSystemWatcher(trigger.ProjectPath)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            Filter = string.IsNullOrEmpty(trigger.Glob) ? "*" : trigger.Glob
        };

        var lastFire = DateTime.MinValue;
        var debounce = TimeSpan.FromMilliseconds(Math.Max(0, trigger.DebounceMs));
        FileSystemEventHandler handler = (_, e) =>
        {
            var now = DateTime.UtcNow;
            if (now - lastFire < debounce)
            {
                return;
            }

            lastFire = now;
            _ = engine.ExecuteAsync(entity, "file_change", CancellationToken.None);
            serviceEventHub.Publish(
                ServiceEventTypes.FileChanged,
                new { path = e.FullPath, change = e.ChangeType.ToString() });
        };

        watcher.Created += handler;
        watcher.Changed += handler;
        watcher.Deleted += handler;
        watcher.Renamed += (_, e) => handler(_, e);
        _fileWatchers[entity.Id] = watcher;
    }

    private void DrainEventsForGitTriggers()
    {
        var poll = serviceEventHub.Poll(_lastSeenSequence);
        if (poll.Events.Length == 0)
        {
            return;
        }

        _lastSeenSequence = poll.LatestSequence;

        foreach (var sched in engine.ActiveSchedules)
        {
            switch (sched.Trigger)
            {
                case GitEventTrigger git:
                    foreach (var ev in poll.Events)
                    {
                        if (string.Equals(ev.EventType, git.EventType, StringComparison.OrdinalIgnoreCase))
                        {
                            _ = engine.ExecuteAsync(sched.Entity, "git_event", CancellationToken.None);
                            break;
                        }
                    }
                    break;
                case SessionEndTrigger:
                    foreach (var ev in poll.Events)
                    {
                        if (string.Equals(ev.EventType, ServiceEventTypes.SessionEnd, StringComparison.OrdinalIgnoreCase))
                        {
                            _ = engine.ExecuteAsync(sched.Entity, "session_end", CancellationToken.None);
                            break;
                        }
                    }
                    break;
            }
        }
    }

    private void StartWebhookListener(CancellationToken cancellationToken)
    {
        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://127.0.0.1:{WebhookPort}/");
            _httpListener.Start();
            _ = Task.Run(() => ProcessWebhookRequestsAsync(cancellationToken), cancellationToken);
            logger.LogInformation("Automation webhook listener bound to {Port}.", WebhookPort);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Automation webhook listener could not start; webhook triggers disabled.");
            _httpListener = null;
        }
    }

    private async Task ProcessWebhookRequestsAsync(CancellationToken cancellationToken)
    {
        if (_httpListener is null) { return; }

        while (!cancellationToken.IsCancellationRequested && _httpListener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _httpListener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                break;
            }

            try
            {
                var path = context.Request.Url?.AbsolutePath ?? string.Empty;
                if (_webhookRoutes.TryGetValue(path, out var automationId))
                {
                    var sched = engine.ActiveSchedules.FirstOrDefault(s => s.Entity.Id == automationId);
                    if (sched is not null)
                    {
                        _ = engine.ExecuteAsync(sched.Entity, "webhook", CancellationToken.None);
                    }
                    context.Response.StatusCode = 202;
                }
                else
                {
                    context.Response.StatusCode = 404;
                }

                var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ok = true }, JsonSerialization.Options));
                context.Response.OutputStream.Write(body, 0, body.Length);
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    private void StopWebhookListener()
    {
        try
        {
            _httpListener?.Stop();
            _httpListener?.Close();
        }
        catch (Exception)
        {
            // Listener already closed.
        }
        _httpListener = null;
    }

    private void DisposeAllWatchers()
    {
        foreach (var watcher in _fileWatchers.Values)
        {
            watcher.Dispose();
        }
        _fileWatchers.Clear();
    }
}
