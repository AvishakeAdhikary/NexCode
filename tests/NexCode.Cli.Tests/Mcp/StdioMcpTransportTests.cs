using System.IO;
using System.Text;
using NexCode.Service.Mcp;

namespace NexCode.Cli.Tests.Mcp;

public sealed class StdioMcpTransportTests
{
    [Fact]
    public void FrameForTesting_BuildsLspContentLengthHeader()
    {
        var bytes = StdioMcpTransport.FrameForTesting("{\"a\":1}");
        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("Content-Length: 7\r\n\r\n", text);
        Assert.EndsWith("{\"a\":1}", text);
    }

    [Fact]
    public async Task FrameReader_ParsesSingleFrame()
    {
        var payload = "{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"result\":{\"ok\":true}}";
        var bytes = StdioMcpTransport.FrameForTesting(payload);
        using var ms = new MemoryStream(bytes);
        var reader = new McpFrameReader(ms);

        var frame = await reader.ReadFrameAsync(CancellationToken.None);

        Assert.Equal(payload, frame);
    }

    [Fact]
    public async Task FrameReader_ParsesTwoConsecutiveFrames()
    {
        var first = "{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"result\":{\"a\":1}}";
        var second = "{\"jsonrpc\":\"2.0\",\"id\":\"2\",\"result\":{\"b\":2}}";

        var bytes = new List<byte>();
        bytes.AddRange(StdioMcpTransport.FrameForTesting(first));
        bytes.AddRange(StdioMcpTransport.FrameForTesting(second));

        using var ms = new MemoryStream(bytes.ToArray());
        var reader = new McpFrameReader(ms);

        var f1 = await reader.ReadFrameAsync(CancellationToken.None);
        var f2 = await reader.ReadFrameAsync(CancellationToken.None);
        var f3 = await reader.ReadFrameAsync(CancellationToken.None);

        Assert.Equal(first, f1);
        Assert.Equal(second, f2);
        Assert.Null(f3);
    }

    [Fact]
    public async Task FrameReader_HandlesNonContentHeaders()
    {
        var payload = "{\"v\":1}";
        var prefix = Encoding.ASCII.GetBytes(
            $"Content-Type: application/vscode-jsonrpc\r\nContent-Length: {Encoding.UTF8.GetByteCount(payload)}\r\n\r\n");
        var body = Encoding.UTF8.GetBytes(payload);

        using var ms = new MemoryStream();
        ms.Write(prefix);
        ms.Write(body);
        ms.Position = 0;

        var reader = new McpFrameReader(ms);
        var frame = await reader.ReadFrameAsync(CancellationToken.None);

        Assert.Equal(payload, frame);
    }
}
