using System.Text.Json;
using System.Text.Json.Nodes;

namespace Core.ChatCompletions
{
    public enum OpenAiReasoningEffort
    {
        None,
        Minimal,
        Low,
        Medium,
        High,
        XHigh
    }

    public enum GptOssReasoningEffort
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

    public abstract class Request
    {
        public StructuredOutputOptions? StructuredOutput { get; set; }
        public List<ChatCompletionMessageParam> Messages { get; set; } = new();

        public List<ChatCompletionTool> Tools { get; set; } = new();

        public string ToJson()
        {
            JsonObject body = new JsonObject()
            {
                ["messages"] = new JsonArray(Messages.ConvertAll(m => (JsonNode?)m.ToJson()).ToArray()),
            };

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

            AddModelSpecificFields(body);
            AddStructuredOutputFields(body);

            return body.ToJsonString();
        }

        protected abstract void AddModelSpecificFields(JsonObject body);

        private void AddStructuredOutputFields(JsonObject body)
        {
            if (string.IsNullOrWhiteSpace(StructuredOutput?.OutputMode))
            {
                return;
            }

            if (!string.Equals(StructuredOutput.OutputMode, "json_schema", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string schemaName = string.IsNullOrWhiteSpace(StructuredOutput.JsonSchemaName) ? "structured_response" : StructuredOutput.JsonSchemaName;
            JsonObject jsonSchemaBody = new JsonObject
            {
                ["name"] = schemaName,
                ["schema"] = JsonNode.Parse(StructuredOutput.JsonSchema!.Value.GetRawText())
            };

            jsonSchemaBody["strict"] = StructuredOutput.JsonStrict ?? true;

            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = jsonSchemaBody
            };
        }
    }

    public sealed class OpenAiRequest : Request
    {
        public string? Model { get; set; }
        public OpenAiReasoningEffort? ReasoningEffort { get; set; }
        public Verbosity? Verbosity { get; set; }
        public ServiceTier? ServiceTier { get; set; }
        public string? PromptCacheKey { get; set; }

        protected override void AddModelSpecificFields(JsonObject body)
        {
            if (!string.IsNullOrEmpty(Model)) body["model"] = Model;
            if (!string.IsNullOrEmpty(PromptCacheKey)) body["prompt_cache_key"] = PromptCacheKey;
            if (ReasoningEffort != null) body["reasoning_effort"] = ReasoningEffort.Value.ToString().ToLowerInvariant();
            if (Verbosity != null) body["verbosity"] = Verbosity.Value.ToString().ToLowerInvariant();
            if (ServiceTier != null) body["service_tier"] = ServiceTier.Value.ToString().ToLowerInvariant();

        }
    }

    public sealed class GptOssRequest : Request
    {
        public GptOssReasoningEffort? ReasoningEffort { get; set; }

        protected override void AddModelSpecificFields(JsonObject body)
        {
            if (ReasoningEffort != null)
            {
                body["chat_template_kwargs"] = new JsonObject
                {
                    ["reasoning_effort"] = ReasoningEffort.Value.ToString().ToLowerInvariant()
                };
            }
        }
    }

    public sealed class QwenRequest : Request
    {
        public bool? EnableThinking { get; set; }

        protected override void AddModelSpecificFields(JsonObject body)
        {
            if (EnableThinking != null)
            {
                body["chat_template_kwargs"] = new JsonObject
                {
                    ["enable_thinking"] = EnableThinking.Value
                };
            }
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
