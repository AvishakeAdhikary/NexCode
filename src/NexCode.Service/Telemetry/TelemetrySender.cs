using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexCode.Shared.Contracts;
using NexCode.Shared.Json;

namespace NexCode.Service.Telemetry;

/// <summary>
/// Hosted background service that drains <see cref="TelemetryQueue"/> in batches whenever
/// (a) the helper starts, (b) every 6 hours, and (c) on session end (signaled via
/// <see cref="ServiceEventHub"/>). Honors a 50 KB/s rate limit and exponential backoff
/// (capped at 7 days) and obeys the <c>NEXCODE_TELEMETRY_ENDPOINT</c> opt-in envar.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TelemetrySender(
    TelemetryQueue queue,
    TelemetryConsentService consent,
    IHttpClientFactory httpClientFactory,
    ServiceEventHub serviceEventHub,
    ILogger<TelemetrySender> logger) : BackgroundService
{
    private const int BatchSize = 64;
    private const int RateLimitBytesPerSecond = 50 * 1024;
    private static readonly TimeSpan FlushPeriod = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromDays(7);

    private TimeSpan _backoff = TimeSpan.FromMinutes(1);
    private long _lastSeenSequence;

    public string? Endpoint => Environment.GetEnvironmentVariable("NEXCODE_TELEMETRY_ENDPOINT");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TelemetrySender started; endpoint configured = {HasEndpoint}.", Endpoint is not null);
        await TryFlushAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var delay = Task.Delay(FlushPeriod, delayCts.Token);
                var sessionEndTask = WatchForSessionEndAsync(delayCts.Token);
                var completed = await Task.WhenAny(delay, sessionEndTask).ConfigureAwait(false);
                delayCts.Cancel();
                _ = completed; // Either path triggers a flush.
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await TryFlushAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task WatchForSessionEndAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var poll = serviceEventHub.Poll(_lastSeenSequence);
            _lastSeenSequence = poll.LatestSequence;
            if (poll.Events.Any(e =>
                    string.Equals(e.EventType, ServiceEventTypes.SessionEnd, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
        }
    }

    private async Task TryFlushAsync(CancellationToken cancellationToken)
    {
        var endpoint = Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint)) { return; }
        if (!await consent.GetAsync(cancellationToken).ConfigureAwait(false)) { return; }

        var batch = await queue.DrainAsync(BatchSize, cancellationToken).ConfigureAwait(false);
        if (batch.Count == 0) { return; }

        var payloadJson = JsonSerializer.Serialize(batch.Select(b => b.Event).ToArray(), JsonSerialization.Options);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        // Crude rate limit: throttle so we never push more than RateLimitBytesPerSecond.
        var minMillis = (int)((double)payloadBytes.Length / RateLimitBytesPerSecond * 1000);
        if (minMillis > 0)
        {
            try
            {
                await Task.Delay(minMillis, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        try
        {
            var client = httpClientFactory.CreateClient(nameof(TelemetrySender));
            using var content = new ByteArrayContent(payloadBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await client.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                await queue.AckAsync(batch.Select(b => b.Id), cancellationToken).ConfigureAwait(false);
                _backoff = TimeSpan.FromMinutes(1);
                return;
            }

            var status = (int)response.StatusCode;
            if (status >= 400 && status < 500)
            {
                logger.LogWarning("Telemetry server rejected batch with {Status}; dropping.", status);
                await queue.AckAsync(batch.Select(b => b.Id), cancellationToken).ConfigureAwait(false);
                _backoff = TimeSpan.FromMinutes(1);
                return;
            }

            logger.LogWarning("Telemetry transmit failed with {Status}; backing off.", status);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telemetry transmit threw; backing off.");
        }

        _backoff = TimeSpan.FromTicks(Math.Min(_backoff.Ticks * 2, MaxBackoff.Ticks));
        try
        {
            await Task.Delay(_backoff, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }
}
