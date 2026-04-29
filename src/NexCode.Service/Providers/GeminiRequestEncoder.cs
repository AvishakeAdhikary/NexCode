using System.Text;
using System.Text.Json;

namespace NexCode.Service.Providers;

/// <summary>
/// Wire-format encoder for Google Gemini's <c>generateContent</c> family. Split out from
/// <see cref="GeminiProvider"/> to keep the streaming adapter close to spec's 300-line
/// budget.
/// </summary>
internal static class GeminiRequestEncoder
{
    public static string BuildRequestBody(ProviderTurnRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                writer.WritePropertyName("systemInstruction");
                writer.WriteStartObject();
                writer.WritePropertyName("parts");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("text", request.SystemPrompt);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WritePropertyName("generationConfig");
            writer.WriteStartObject();
            writer.WriteNumber("maxOutputTokens", request.MaxOutputTokens);
            writer.WriteNumber("temperature", request.Temperature);
            writer.WriteEndObject();

            writer.WritePropertyName("contents");
            writer.WriteStartArray();
            foreach (var message in request.Conversation)
            {
                WriteContent(writer, message);
            }
            writer.WriteEndArray();

            if (request.Tools.Count > 0)
            {
                writer.WritePropertyName("tools");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WritePropertyName("functionDeclarations");
                writer.WriteStartArray();
                foreach (var tool in request.Tools)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", tool.Name);
                    writer.WriteString("description", tool.Description);
                    writer.WritePropertyName("parameters");
                    tool.InputSchema.WriteTo(writer);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteContent(Utf8JsonWriter writer, ProviderConversationMessage message)
    {
        if (message.Role == ProviderMessageRole.System)
        {
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("role", message.Role switch
        {
            ProviderMessageRole.Assistant => "model",
            ProviderMessageRole.Tool => "user",
            _ => "user",
        });
        writer.WritePropertyName("parts");
        writer.WriteStartArray();

        if (!string.IsNullOrEmpty(message.Content))
        {
            writer.WriteStartObject();
            writer.WriteString("text", message.Content);
            writer.WriteEndObject();
        }

        if (message.Role == ProviderMessageRole.Assistant && message.ToolUses is { Count: > 0 } uses)
        {
            foreach (var use in uses)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("functionCall");
                writer.WriteStartObject();
                writer.WriteString("name", use.ToolName);
                writer.WritePropertyName("args");
                if (string.IsNullOrWhiteSpace(use.ArgumentsJson))
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                }
                else
                {
                    using var doc = JsonDocument.Parse(use.ArgumentsJson);
                    doc.RootElement.WriteTo(writer);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
        }

        if (message.Role == ProviderMessageRole.Tool && message.ToolResults is { Count: > 0 } results)
        {
            foreach (var result in results)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("functionResponse");
                writer.WriteStartObject();
                writer.WriteString("name", result.CallId);
                writer.WritePropertyName("response");
                if (string.IsNullOrWhiteSpace(result.ResultJson))
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                }
                else
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(result.ResultJson);
                        doc.RootElement.WriteTo(writer);
                    }
                    catch
                    {
                        writer.WriteStartObject();
                        writer.WriteString("content", result.ResultJson);
                        writer.WriteEndObject();
                    }
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
