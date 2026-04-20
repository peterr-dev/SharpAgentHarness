using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Core.ChatCompletions
{
    /// <summary>
    /// Base class for all LLM responses.
    /// </summary>
    public abstract class Response
    {
        public string RawBody { get; }

        protected Response(string rawBody)
        {
            RawBody = rawBody;
        }

        public static Response Parse(string rawBody)
        {
            // Convert the raw body to a JSON object to check for error fields.
            JsonNode json = JsonNode.Parse(rawBody) ?? throw new InvalidOperationException("Failed to parse LLM response body as JSON.");

            if (json["error"] == null)
            {
                return new SuccessResponse(rawBody, json);
            }
            else
            {
                return new ErrorResponse(rawBody, json);
            }
        }
    }
    /// <summary>
    /// A successful LLM response
    /// </summary>
    public class SuccessResponse : Response
    {
        public string Id { get; init; }
        
        public string Object { get; init; }

        public long Created { get; init; }

        public string Model { get; init; }

        public List<ChatCompletionChoice> Choices { get; init; } = new();

        public ChatCompletionUsage Usage { get; init; } = new();

        public SuccessResponse(string rawBody, JsonNode json) : base(rawBody)
        {
            Id = json["id"]?.ToString() ?? throw new InvalidOperationException("LLM response JSON does not contain 'id' field.");
            Model = json["model"]?.ToString() ?? throw new InvalidOperationException("LLM response JSON does not contain 'model' field.");
            Object = json["object"]?.ToString() ?? throw new InvalidOperationException("LLM response JSON does not contain 'object' field.");
            Created = json["created"]?.GetValue<long>() ?? throw new InvalidOperationException("LLM response JSON does not contain 'created' field.");
            
            Choices = json["choices"]?.AsArray()?.Select(c => new ChatCompletionChoice
            {
                Index = c!["index"]?.GetValue<int>() ?? throw new InvalidOperationException("LLM response choice JSON does not contain 'index' field."),
                FinishReason = ParseFinishReason(
                    c["finish_reason"]?.ToString() ?? throw new InvalidOperationException("LLM response choice JSON does not contain 'finish_reason' field.")),
                Message = new ChatCompletionMessage
                {
                    Role = c["message"]?["role"]?.ToString() ?? throw new InvalidOperationException("LLM response choice message JSON does not contain 'role' field."),
                    Content = ParseAssistantContent(c["message"]?["content"]),
                    Refusal = c["message"]?["refusal"]?.ToString() ?? string.Empty, // 'refusal' is optional and may be empty if the model did not refuse to answer.
                    ToolCalls = c["message"]?["tool_calls"]?.AsArray()?.Select(tc =>
                    {
                        string type = tc?["type"]?.ToString()
                            ?? throw new InvalidOperationException("LLM response choice message tool call JSON does not contain 'type' field.");

                        return type switch
                        {
                            "function" => new ChatCompletionMessageFunctionCall
                            {
                                Id = tc["id"]?.ToString()
                                    ?? throw new InvalidOperationException("LLM response choice message tool call JSON does not contain 'id' field."),
                                FunctionName = tc["function"]?["name"]?.ToString()
                                    ?? throw new InvalidOperationException("LLM response choice message tool call JSON does not contain 'function.name' field."),
                                Arguments = tc["function"]?["arguments"]?.GetValue<string>() ?? string.Empty
                            },
                            _ => throw new InvalidOperationException($"Unsupported tool call type: {type}")
                        };
                    }).Cast<ChatCompletionMessageToolCall>().ToList()
                }
            }).ToList() ?? throw new InvalidOperationException("LLM response JSON does not contain 'choices' field or it is not an array.");

            Usage = ChatCompletionUsage.FromJson(json["usage"]);
        }

        private static string? ParseAssistantContent(JsonNode? contentJson)
        {
            if (contentJson is null)
            {
                return null;
            }

            // Chat Completions may return either a plain string or an array of content parts.
            // When parts are used, we only keep visible text-like outputs and skip reasoning parts.
            if (contentJson is JsonValue)
            {
                return contentJson.GetValue<string>();
            }

            if (contentJson is JsonArray contentParts)
            {
                List<string> textParts = contentParts
                    .Select(part =>
                    {
                        string? type = part?["type"]?.ToString();
                        return type switch
                        {
                            "text" => part?["text"]?.ToString(),
                            "output_text" => part?["text"]?.ToString(),
                            _ => null
                        };
                    })
                    .Where(text => !string.IsNullOrEmpty(text))
                    .Cast<string>()
                    .ToList();

                return textParts.Count > 0 ? string.Concat(textParts) : null;
            }

            throw new InvalidOperationException("Unsupported 'message.content' JSON shape in LLM response.");
        }

        private static FinishReason ParseFinishReason(string finishReason)
        {
            return finishReason switch
            {
                "stop" => FinishReason.Stop,
                "length" => FinishReason.Length,
                "content_filter" => FinishReason.ContentFilter,
                "tool_calls" => FinishReason.ToolCalls,
                _ => throw new InvalidOperationException($"Unsupported finish_reason: {finishReason}")
            };
        }
    }

    public sealed class ChatCompletionUsage
    {
        public int InputTokens { get; init; }

        public int CachedInputTokens { get; init; }

        public int OutputTokens { get; init; }

        public int ReasoningOutputTokens { get; init; }

        public static ChatCompletionUsage FromJson(JsonNode? usageJson)
        {
            // Usage fields are optional. Missing fields are treated as zero.
            return new ChatCompletionUsage
            {
                InputTokens = usageJson?["prompt_tokens"]?.GetValue<int>() ?? 0,
                OutputTokens = usageJson?["completion_tokens"]?.GetValue<int>() ?? 0,
                CachedInputTokens = usageJson?["prompt_tokens_details"]?["cached_tokens"]?.GetValue<int>() ?? 0,
                ReasoningOutputTokens = usageJson?["completion_tokens_details"]?["reasoning_tokens"]?.GetValue<int>() ?? 0
            };
        }
    }

    public enum FinishReason
    {
        Stop,
        Length,
        ContentFilter,
        ToolCalls
    }

    public class ChatCompletionChoice
    {
        public int Index { get; init; }

        public required ChatCompletionMessage Message { get; init; }

        public FinishReason FinishReason { get; init; }
    }

    public class ChatCompletionMessage
    {
        public required string Role { get; init; }

        public string? Content { get; init; }

        public required string Refusal { get; init; }

        public List<ChatCompletionMessageToolCall>? ToolCalls { get; init; }
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(ChatCompletionMessageFunctionCall), "function")]
    public abstract class ChatCompletionMessageToolCall
    {
        public required string Id { get; init; }

        public abstract JsonObject ToJson();
    }

    public class ChatCompletionMessageFunctionCall : ChatCompletionMessageToolCall
    {
        public required string FunctionName { get; init; }

        public string? Arguments { get; init; }

        public override JsonObject ToJson()
        {
            return new JsonObject
            {
                ["id"] = Id,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = FunctionName,
                    ["arguments"] = Arguments
                }
            };
        }
    }

    /// <summary>
    /// A failed LLM response
    /// </summary>
    public class ErrorResponse : Response
    {
        public string Message { get; init; }

        public string Type { get; init; }

        public string Param { get; init; }

        public string Code { get; init; }

        public ErrorResponse(string rawBody, JsonNode json) : base(rawBody)
        {
            Message = json["error"]?["message"]?.ToString() ?? string.Empty;
            Type = json["error"]?["type"]?.ToString() ?? string.Empty;
            Param = json["error"]?["param"]?.ToString() ?? string.Empty;
            Code = json["error"]?["code"]?.ToString() ?? string.Empty;
        }
    }
}
