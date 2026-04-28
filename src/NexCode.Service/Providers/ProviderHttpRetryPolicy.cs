using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace NexCode.Service.Providers;

/// <summary>
/// Wraps an <see cref="HttpClient"/> send with exponential-backoff retries on transient HTTP
/// failures (429 and 5xx). Honors <c>Retry-After</c> headers (seconds or HTTP-date), capped at
/// <see cref="MaxRetryDelay"/>. Each retry surfaces a <see cref="ProviderRetryEvent"/> via the
/// caller-provided sink so the streaming provider can forward it to the agent loop.
/// </summary>
public sealed class ProviderHttpRetryPolicy
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public ProviderHttpRetryPolicy()
        : this(static () => DateTimeOffset.UtcNow, static (delay, ct) => Task.Delay(delay, ct))
    {
    }

    /// <summary>Test seam — allows tests to control the clock and skip real waits.</summary>
    public ProviderHttpRetryPolicy(
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _utcNow = utcNow;
        _delay = delay;
    }

    /// <summary>
    /// Sends <paramref name="requestFactory"/>'s request, retrying up to <see cref="MaxAttempts"/>
    /// times. Returns the final <see cref="HttpResponseMessage"/> (success or unretryable failure).
    /// Returns <c>null</c> when retries are exhausted on transient failures — the caller should
    /// translate that into a <see cref="ProviderErrorEvent"/>.
    /// </summary>
    public async Task<HttpResponseMessage?> SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        Action<ProviderRetryEvent> onRetry,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? lastResponse = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = requestFactory();
            var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!IsTransient(response.StatusCode))
            {
                return response;
            }

            lastResponse?.Dispose();
            lastResponse = response;

            if (attempt == MaxAttempts)
            {
                break;
            }

            var delay = ComputeDelay(response.Headers.RetryAfter, attempt);
            onRetry(new ProviderRetryEvent(
                Attempt: attempt,
                Reason: $"HTTP {(int)response.StatusCode}",
                Delay: delay));
            response.Dispose();
            lastResponse = null;
            await _delay(delay, cancellationToken).ConfigureAwait(false);
        }

        lastResponse?.Dispose();
        return null;
    }

    private TimeSpan ComputeDelay(RetryConditionHeaderValue? retryAfter, int attempt)
    {
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                return Cap(delta);
            }
            if (retryAfter.Date is { } date)
            {
                var diff = date - _utcNow();
                if (diff > TimeSpan.Zero)
                {
                    return Cap(diff);
                }
            }
        }

        var backoffSeconds = Math.Pow(2, attempt - 1);
        return Cap(TimeSpan.FromSeconds(backoffSeconds));
    }

    private static TimeSpan Cap(TimeSpan value)
    {
        return value > MaxRetryDelay ? MaxRetryDelay : value;
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }
        var code = (int)statusCode;
        return code >= 500 && code < 600;
    }

    /// <summary>Helper for tests / providers needing to format a Retry-After seconds header.</summary>
    public static string FormatSecondsHeader(int seconds)
        => seconds.ToString(CultureInfo.InvariantCulture);
}
