using System.Text.Json;
using NexCode.Shared.Json;

namespace NexCode.Shared.Ipc;

public sealed record JsonRpcRequest(
    string Jsonrpc,
    string Method,
    JsonElement? Params,
    string? Id)
{
    public static JsonRpcRequest Create<TParameters>(string method, TParameters parameters, string? id = null)
    {
        return new JsonRpcRequest(
            Jsonrpc: "2.0",
            Method: method,
            Params: JsonSerializer.SerializeToElement(parameters, JsonSerialization.Options),
            Id: id ?? Guid.NewGuid().ToString("N"));
    }

    public TParameters? DeserializeParams<TParameters>()
    {
        return Params is null
            ? default
            : Params.Value.Deserialize<TParameters>(JsonSerialization.Options);
    }
}

public sealed record JsonRpcResponse(
    string Jsonrpc,
    string? Id,
    JsonElement? Result,
    JsonRpcError? Error)
{
    public static JsonRpcResponse Success<TResult>(string? id, TResult result)
    {
        return new JsonRpcResponse(
            Jsonrpc: "2.0",
            Id: id,
            Result: JsonSerializer.SerializeToElement(result, JsonSerialization.Options),
            Error: null);
    }

    public static JsonRpcResponse Failure(string? id, int code, string message)
    {
        return new JsonRpcResponse(
            Jsonrpc: "2.0",
            Id: id,
            Result: null,
            Error: new JsonRpcError(code, message));
    }

    public TResult? DeserializeResult<TResult>()
    {
        return Result is null
            ? default
            : Result.Value.Deserialize<TResult>(JsonSerialization.Options);
    }
}

public sealed record JsonRpcError(int Code, string Message);
