using System.Text.Json.Nodes;

namespace Core.ChatCompletions
{
    public enum ReasoningEffort
    {
        None,
        Minimal,
        Low,
        Medium,
        High,
        XHigh
    }

    public enum LocalReasoningEffort
    {
        Low,
        Medium,
        High
    }

    public enum Verbosity
    {
        Low,
        Medium,
        High
    }

    public enum ServiceTier
    {
        Auto,
        Default,
        Flex,
        Scale,
        Priority
    }

    public class Request
    {
        public List<ChatCompletionMessageParam> Messages { get; set; } = new();

        public List<ChatCompletionTool> Tools { get; set; } = new();

        public double? Temperature { get; set; }

        public string? Model { get; set; }

        public ReasoningEffort? ReasoningEffort { get; set; }

        public LocalReasoningEffort? LocalReasoningEffort { get; set; }

        public Verbosity? Verbosity { get; set; }

        public ServiceTier? ServiceTier { get; set; }

        public string? PromptCacheKey { get; set; }

        public string ToJson()
        {
            JsonObject body = new JsonObject()
            {
                ["messages"] = new JsonArray(Messages.ConvertAll(m => (JsonNode?)m.ToJson()).ToArray()),
            };

            if (Temperature != null)
                body["temperature"] = Temperature;

            if (!string.IsNullOrEmpty(Model))
                body["model"] = Model;

            if (!string.IsNullOrEmpty(PromptCacheKey))
                body["prompt_cache_key"] = PromptCacheKey;

            if (ReasoningEffort != null)
                body["reasoning_effort"] = ReasoningEffort.Value.ToString().ToLowerInvariant();

            if (Verbosity != null)
                body["verbosity"] = Verbosity.Value.ToString().ToLowerInvariant();   
            
            if (ServiceTier != null)
                body["service_tier"] = ServiceTier.Value.ToString().ToLowerInvariant();

            Tools.ForEach(tool =>
            {
                if (tool is ChatCompletionFunctionTool functionTool)
                {
                    JsonObject properties = new JsonObject();
                    JsonArray required = new JsonArray();

                    foreach (FunctionToolParameter parameter in functionTool.Parameters)
                    {
                        string jsonType = parameter.Type switch
                        {
                            FunctionToolCallParameterType.String => "string",
                            FunctionToolCallParameterType.Number => "number",
                            FunctionToolCallParameterType.Boolean => "boolean",
                            _ => throw new InvalidOperationException($"Unsupported parameter type: {parameter.Type}")
                        };

                        properties[parameter.Name] = new JsonObject
                        {
                            ["type"] = jsonType,
                            ["description"] = parameter.Description
                        };

                        required.Add(parameter.Name);
                    }

                    JsonObject toolJson = new JsonObject
                    {
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = functionTool.Name,
                            ["description"] = functionTool.Description,
                            ["strict"] = functionTool.Strict,
                            ["parameters"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = properties,
                                ["required"] = required,
                                ["additionalProperties"] = false
                            }
                        }
                    };

                    ((JsonArray)(body["tools"] ??= new JsonArray())).Add(toolJson);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported tool type: {tool.GetType().Name}");
                }
            });

            if (LocalReasoningEffort != null)
            {
                body["chat_template_kwargs"] = new JsonObject
                {
                    ["reasoning_effort"] = LocalReasoningEffort.Value.ToString().ToLowerInvariant()
                };
            }

            return body.ToJsonString();
        }
    }

    #region Messages

    public abstract class ChatCompletionMessageParam
    {
        public abstract JsonObject ToJson();
    }

    public sealed class ChatCompletionDeveloperMessageParam : ChatCompletionMessageParam
    {
        public required string Content { get; init; }

        public override JsonObject ToJson()
        {
            return new JsonObject
            {
                ["role"] = "developer",
                ["content"] = Content
            };
        }
    }

    public sealed class ChatCompletionUserMessageParam : ChatCompletionMessageParam
    {
        public List<ChatCompletionContentPart> Content { get; init; } = new();

        public override JsonObject ToJson()
        {
            return new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(Content.ConvertAll(c => (JsonNode?)c.ToJson()).ToArray())
            };
        }
    }

    public abstract class ChatCompletionContentPart
    {
        public abstract JsonObject ToJson();
    }

    public sealed class ChatCompletionContentPartText : ChatCompletionContentPart
    {
        public required string Text { get; init; }

        public override JsonObject ToJson()
        {
            return new JsonObject
            {
                ["type"] = "text",
                ["text"] = Text
            };
        }
    }

    public sealed class ChatCompletionAssistantMessageParam : ChatCompletionMessageParam
    {
        public List<ChatCompletionContentPart>? Content { get; init; }

        public string? ReasoningContent { get; init; }

        public List<ChatCompletionMessageToolCall>? ToolCalls { get; init; }

        public override JsonObject ToJson()
        {
            JsonObject result = new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = Content != null ? new JsonArray(Content.ConvertAll(c => (JsonNode?)c.ToJson()).ToArray()) : null
            };

            if (ToolCalls != null)
            {
                result["tool_calls"] = new JsonArray(ToolCalls.ConvertAll(tc => (JsonNode?)tc.ToJson()).ToArray());
            }

            if (!string.IsNullOrEmpty(ReasoningContent))
            {
                result["reasoning_content"] = ReasoningContent;
            }

            return result;
        }
    }

    #endregion

    #region Tools

    public abstract class ChatCompletionTool
    {
        public required string Name { get; init; }

        public async virtual Task<string> ExecuteAsync(string argumentsJson)
        {
            await Task.CompletedTask;
            return string.Empty;
        }
    }

    public class ChatCompletionFunctionTool : ChatCompletionTool
    {
        public required string Description { get; init; }

        public required bool Strict { get; init; }

        public List<FunctionToolParameter> Parameters { get; init; } = new();
    }

    public enum FunctionToolCallParameterType
    {
        String,
        Number,
        Boolean
    }

    public class FunctionToolParameter
    {
        public required string Name { get; init; }

        public required string Description { get; init; }

        public required FunctionToolCallParameterType Type { get; init; }
    }

    public sealed class ChatCompletionToolMessageParam : ChatCompletionMessageParam
    {
        public required string ToolCallId { get; init; }

        public required string Content { get; init; }

        public override JsonObject ToJson()
        {
            return new JsonObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = ToolCallId,
                ["content"] = Content
            };
        }
    }

    #endregion
}
