using System.Globalization;
using System.IO;
using System.Text;

namespace NexCode.Service.Mcp;

/// <summary>
/// LSP-style framed message reader: parses <c>Content-Length: N</c> headers, an empty line,
/// then exactly N bytes of UTF-8 body. Shared between <see cref="StdioMcpTransport"/> and
/// transport tests.
/// </summary>
public sealed class McpFrameReader(Stream stream)
{
    private readonly Stream _stream = stream;
    private readonly StringBuilder _headerBuilder = new();

    public async Task<string?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        _headerBuilder.Clear();
        int contentLength = -1;

        while (true)
        {
            var line = await ReadHeaderLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                if (contentLength < 0)
                {
                    return null;
                }
                break;
            }

            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var len))
                {
                    contentLength = len;
                }
            }
        }

        if (contentLength <= 0)
        {
            return string.Empty;
        }

        var buffer = new byte[contentLength];
        var read = 0;
        while (read < contentLength)
        {
            var n = await _stream.ReadAsync(
                buffer.AsMemory(read, contentLength - read),
                cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                return null;
            }
            read += n;
        }

        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private async Task<string?> ReadHeaderLineAsync(CancellationToken cancellationToken)
    {
        _headerBuilder.Clear();
        var single = new byte[1];
        var lastWasCr = false;
        while (true)
        {
            var read = await _stream.ReadAsync(single.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return _headerBuilder.Length == 0 ? null : _headerBuilder.ToString();
            }

            var ch = (char)single[0];
            if (ch == '\n')
            {
                return _headerBuilder.ToString();
            }
            if (ch == '\r')
            {
                lastWasCr = true;
                continue;
            }
            if (lastWasCr)
            {
                _headerBuilder.Append('\r');
                lastWasCr = false;
            }
            _headerBuilder.Append(ch);
        }
    }

    /// <summary>Helper used by tests to build an LSP-framed wire message.</summary>
    public static byte[] FrameForTesting(string payload)
    {
        var body = Encoding.UTF8.GetBytes(payload);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        var result = new byte[header.Length + body.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(body, 0, result, header.Length, body.Length);
        return result;
    }
}
