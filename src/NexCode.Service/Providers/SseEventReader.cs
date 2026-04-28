using System.IO;
using System.Text;

namespace NexCode.Service.Providers;

/// <summary>
/// Allocation-light streaming Server-Sent Events parser. Reads <c>event:</c> and <c>data:</c>
/// lines from a <see cref="Stream"/> and emits one <see cref="SseEvent"/> per blank-line
/// terminated record. Multi-line <c>data:</c> values are concatenated with newlines per the
/// SSE spec.
/// </summary>
public sealed class SseEventReader(Stream stream) : IAsyncDisposable
{
    private readonly StreamReader _reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: false);

    /// <summary>
    /// Yields SSE events as they arrive. The enumeration ends when the underlying stream
    /// is exhausted or <paramref name="cancellationToken"/> is signaled.
    /// </summary>
    public async IAsyncEnumerable<SseEvent> ReadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var eventName = string.Empty;
        var dataBuilder = new StringBuilder();
        var hasData = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                if (hasData)
                {
                    yield return new SseEvent(eventName, dataBuilder.ToString());
                }
                yield break;
            }

            if (line.Length == 0)
            {
                if (hasData)
                {
                    yield return new SseEvent(eventName, dataBuilder.ToString());
                }
                eventName = string.Empty;
                dataBuilder.Clear();
                hasData = false;
                continue;
            }

            if (line.StartsWith(":", StringComparison.Ordinal))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            string field;
            string value;
            if (colon < 0)
            {
                field = line;
                value = string.Empty;
            }
            else
            {
                field = line[..colon];
                value = line[(colon + 1)..];
                if (value.Length > 0 && value[0] == ' ')
                {
                    value = value[1..];
                }
            }

            switch (field)
            {
                case "event":
                    eventName = value;
                    break;
                case "data":
                    if (hasData)
                    {
                        dataBuilder.Append('\n');
                    }
                    dataBuilder.Append(value);
                    hasData = true;
                    break;
                default:
                    break;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _reader.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>One parsed SSE record: event name (may be empty) and concatenated data payload.</summary>
public readonly record struct SseEvent(string EventName, string Data);
