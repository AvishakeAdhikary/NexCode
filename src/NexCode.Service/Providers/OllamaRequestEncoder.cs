using System.Text;
using System.Text.Json;

namespace NexCode.Service.Providers;

/// <summary>
/// Wire-format encoder for Ollama's <c>POST /api/chat</c>. Split out from
/// <see cref="OllamaProvider"/> to keep the streaming adapter close to spec's 300-line
/// budget.
/// </summary>
internal static class OllamaRequestEncoder
{
    public static string BuildRequestBody(ProviderTurnRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", request.ModelId);
            writer.WriteBoolean("stream", true);
            writer.WritePropertyName("options");
            writer.WriteStartObject();
            writer.WriteNumber("temperature", request.Temperature);
            writer.WriteNumber("num_predict", request.MaxOutputTokens);
            writer.WriteEndObject();

            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                writer.WriteStartObject();
                writer.WriteString("role", "system");
                writer.WriteString("content", request.SystemPrompt);
                writer.WriteEndObject();
            }
            foreach (var message in request.Conversation)
            {
                WriteMessage(writer, message);
            }
            writer.WriteEndArray();

            if (request.Tools.Count > 0)
            {
                writer.WritePropertyName("tools");
                writer.WriteStartArray();
                foreach (var tool in request.Tools)
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "function");
                    writer.WritePropertyName("function");
                    writer.WriteStartObject();
                    writer.WriteString("name", tool.Name);
                    writer.WriteString("description", tool.Description);
                    writer.WritePropertyName("parameters");
                    tool.InputSchema.WriteTo(writer);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteMessage(Utf8JsonWriter writer, ProviderConversationMessage message)
    {
        switch (message.Role)
        {
            case ProviderMessageRole.System:
                writer.WriteStartObject();
                writer.WriteString("role", "system");
                writer.WriteString("content", message.Content ?? string.Empty);
                writer.WriteEndObject();
                return;
            case ProviderMessageRole.Assistant:
                writer.WriteStartObject();
                writer.WriteString("role", "assistant");
                writer.WriteString("content", message.Content ?? string.Empty);
                writer.WriteEndObject();
                return;
            case ProviderMessageRole.Tool:
                if (message.ToolResults is { Count: > 0 } results)
                {
                    foreach (var r in results)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("role", "tool");
                        writer.WriteString("content", r.ResultJson);
                        writer.WriteEndObject();
                    }
                }
                return;
            case ProviderMessageRole.User:
            default:
                writer.WriteStartObject();
                writer.WriteString("role", "user");
                writer.WriteString("content", message.Content ?? string.Empty);
                writer.WriteEndObject();
                return;
        }
    }
}
