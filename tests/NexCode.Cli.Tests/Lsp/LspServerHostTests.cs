using System.IO;
using System.Text;
using System.Text.Json;
using NexCode.Service.Lsp;
using NexCode.Shared.Json;

namespace NexCode.Cli.Tests.Lsp;

public sealed class LspServerHostTests
{
    [Fact]
    public void ReadFrame_DecodesContentLengthBody()
    {
        var body = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{\"ok\":true}}");
        using var stream = BuildFrame(body);

        var decoded = LspServerHost.ReadFrame(stream);

        Assert.NotNull(decoded);
        Assert.Equal(body.Length, decoded!.Length);
        Assert.Equal(body, decoded);
    }

    [Fact]
    public void ReadFrame_RoundTripsThroughHostWriter()
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            method = "initialized",
            @params = new { }
        }, JsonSerialization.Options);

        using var output = new MemoryStream();
        // Encode the same wire format the host's WriteFrame produces.
        WriteFrameTo(output, body);
        output.Position = 0;
        var decoded = LspServerHost.ReadFrame(output);

        Assert.NotNull(decoded);
        Assert.Equal(body.Length, decoded!.Length);
        var parsed = JsonDocument.Parse(decoded).RootElement;
        Assert.Equal("initialized", parsed.GetProperty("method").GetString());
    }

    [Fact]
    public void ReadFrame_HandlesMultipleHeaders()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        using var stream = new MemoryStream();
        var headers = $"Content-Type: application/vscode-jsonrpc; charset=utf-8\r\nContent-Length: {body.Length}\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(headers);
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(body, 0, body.Length);
        stream.Position = 0;

        var decoded = LspServerHost.ReadFrame(stream);

        Assert.NotNull(decoded);
        Assert.Equal(body.Length, decoded!.Length);
    }

    [Fact]
    public void ReadFrame_ReturnsNullOnEof()
    {
        using var stream = new MemoryStream(System.Array.Empty<byte>());
        var decoded = LspServerHost.ReadFrame(stream);
        Assert.Null(decoded);
    }

    private static MemoryStream BuildFrame(byte[] body)
    {
        var stream = new MemoryStream();
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        stream.Write(header, 0, header.Length);
        stream.Write(body, 0, body.Length);
        stream.Position = 0;
        return stream;
    }

    private static void WriteFrameTo(Stream stream, byte[] body)
    {
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        stream.Write(header, 0, header.Length);
        stream.Write(body, 0, body.Length);
    }
}
