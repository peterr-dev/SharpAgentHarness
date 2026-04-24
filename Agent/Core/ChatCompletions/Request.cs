using Core;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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

    public sealed class Request
    {
        private readonly bool _includeOpenAiHostedParameters;

        public string Model { get; }

        public List<ChatCompletionMessageParam> Messages { get; } = new();

        public ReasoningEffort ReasoningEffort { get; }

        public Verbosity Verbosity { get; }

        public ServiceTier ServiceTier { get; }

        public double? Temperature { get; }

        public int? MaxCompletionTokens { get; }

        public List<ChatCompletionTool> Tools { get; }

        public string PromptCacheKey { get; }

        internal Request(Session session)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));

            Model = session.Model;
            _includeOpenAiHostedParameters = session.ChatCompletionsUrl.ToString() == ApiClient.OpenAIChatCompletionsUrl;
            PromptCacheKey = session.PromptCacheKey;
            ReasoningEffort = session.ReasoningEffort;
            Verbosity = session.Verbosity;
            ServiceTier = session.ServiceTier;
            Temperature = session.Temperature;
            MaxCompletionTokens = session.MaxCompletionTokens;
            Tools = session.Toolkit?.Tools.ToList() ?? new List<ChatCompletionTool>();
        }

        #region OpenAI Chat Completions JSON representation

        public string ToJson()
        {
            if (Messages.Count == 0)
                throw new InvalidOperationException("At least one message is required.");

            JsonObject body = new JsonObject
            {
                ["messages"] = new JsonArray(Messages.ConvertAll(m => (JsonNode?)m.ToJson()).ToArray())
            };

            // Omit OpenAI-hosted-only request fields.
            if (_includeOpenAiHostedParameters)
            {
                body["model"] = Model;
                body["prompt_cache_key"] = PromptCacheKey;
                body["reasoning_effort"] = ReasoningEffort.ToString().ToLower();
                body["verbosity"] = Verbosity.ToString().ToLower();
                body["service_tier"] = ServiceTier.ToString().ToLower();
            }

            if (Temperature.HasValue)
                body["temperature"] = Temperature.Value;

            if (MaxCompletionTokens.HasValue)
                body["max_completion_tokens"] = MaxCompletionTokens.Value;

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

            return body.ToJsonString();
        }

        #endregion
    }

    #region Messages

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(ChatCompletionDeveloperMessageParam), "developer")]
    [JsonDerivedType(typeof(ChatCompletionUserMessageParam), "user")]
    [JsonDerivedType(typeof(ChatCompletionAssistantMessageParam), "assistant")]
    [JsonDerivedType(typeof(ChatCompletionToolMessageParam), "tool")]
    public abstract class ChatCompletionMessageParam
    {
        public abstract JsonObject ToJson();
    }

    public sealed class ChatCompletionDeveloperMessageParam : ChatCompletionMessageParam
    {
        public required string Content { get; init; }

        public required bool UseDeveloperMessageInsteadOfSystem { get; init; }

        public override JsonObject ToJson()
        {
            return new JsonObject
            {
                ["role"] = UseDeveloperMessageInsteadOfSystem ? "developer" : "system",
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

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(ChatCompletionContentPartText), "text")]
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
