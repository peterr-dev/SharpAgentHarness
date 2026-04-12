using Agent.Tools;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent.Llm
{
    /// <summary>
    /// Send System Prompts in the <see cref="Request.Instructions"/> property
    /// </summary>
    // Serialise enum values as readable strings in API responses.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MessageRole
    {
        User,
        Assistant
    }

    abstract public class Message { }

    public class EasyInputMessage : Message
    {
        public required string Content { get; init; }
    }

    public class FunctionCallOutputMessage : Message
    {
        public required string CallId { get; init; }

        public required string Output { get; init; }
    }
    
    public class Request
    {
        public required string Model { get; init; }

        public ServiceTier Tier { get; init; }

        public required string PromptCacheKey { get; init; }

        public ReasoningEffort Reasoning { get; init; }

        public TextVerbosity Verbosity { get; init; }

        /// <summary>
        /// Populated from turn two onwards
        /// </summary>
        public string? PreviousResponseId { get; init; }

        /// <summary>
        /// The Responses equivalent to Chat Completion's System Prompt 
        /// </summary>
        public required string Instructions { get; init; }

        public Toolkit? Toolkit { get; init; }

        public required Message InputMessage { get; init; }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = null,
            WriteIndented = false
        };

        public string ToOpenAiResponsesBody()
        {
            var body = new OpenAiResponsesCreateRequest
            {
                Model = Model,
                PreviousResponseId = PreviousResponseId,
                Instructions = Instructions,
                Input = new List<object> { BuildInputMessage(InputMessage) },
                Tools = BuildToolDefinition(Toolkit),
                PromptCacheKey = PromptCacheKey,
                ParallelToolCalls = false,
                ServiceTier = ToApiValue(Tier),
                Reasoning = new ReasoningConfig { Effort = ToApiValue(Reasoning) },
                Text = new TextConfig
                {
                    Verbosity = ToApiValue(Verbosity)
                }
            };

            return JsonSerializer.Serialize(body, JsonOptions);
        }

        private static object BuildInputMessage(Message inputMessage)
        {
            if (inputMessage is EasyInputMessage easyMessage)
            {
                return new InputUserMessage
                {
                    Role = ToApiValue(MessageRole.User),
                    Content = new List<InputContentItem>
                    {
                        new InputContentItem
                        {
                            Type = "input_text",
                            Text = easyMessage.Content
                        }
                    }
                };
            }

            if (inputMessage is FunctionCallOutputMessage functionCallOutput)
            {
                return new InputFunctionCallOutputItem
                {
                    Type = "function_call_output",
                    CallId = functionCallOutput.CallId,
                    Output = functionCallOutput.Output
                };
            }

            throw new ArgumentException($"Unsupported message type: {inputMessage.GetType().Name}.", nameof(inputMessage));
        }

        private static string ToApiValue(ServiceTier value) => value switch
        {
            ServiceTier.Auto => "auto",
            ServiceTier.Default => "default",
            ServiceTier.Flex => "flex",
            ServiceTier.Priority => "priority",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };

        private static string ToApiValue(ReasoningEffort value) => value switch
        {
            ReasoningEffort.None => "none",
            ReasoningEffort.Minimal => "minimal",
            ReasoningEffort.Low => "low",
            ReasoningEffort.Medium => "medium",
            ReasoningEffort.High => "high",
            ReasoningEffort.XHigh => "xhigh",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };

        private static string ToApiValue(MessageRole value) => value switch
        {
            MessageRole.User => "user",
            MessageRole.Assistant => "assistant",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };

        private static string ToApiValue(TextVerbosity value) => value switch
        {
            TextVerbosity.Low => "low",
            TextVerbosity.Medium => "medium",
            TextVerbosity.High => "high",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };

        private static List<object>? BuildToolDefinition(Toolkit? toolkit)
        {
            if (toolkit is null || toolkit.Tools.Count == 0)
            {
                return null;
            }

            return toolkit.Tools.Select(tool => (object)new
            {

                type = "function",
                name = tool.Name,
                description = tool.Description,
                strict = true,
                parameters = BuildObjectSchema(tool.Parameters)
            }).ToList();
        }

        private static object BuildObjectSchema(IEnumerable<ToolParameter> parameters)
        {
            var parameterList = parameters.ToList();
            string[] required = BuildRequiredList(parameterList);

            var schema = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = parameterList.ToDictionary(
                    parameter => parameter.Name,
                    parameter => BuildParameterSchema(parameter)),
                ["additionalProperties"] = false
            };

            if (required.Length > 0)
            {
                schema["required"] = required;
            }

            return schema;
        }

        private static object BuildParameterSchema(ToolParameter parameter)
        {
            var schema = new Dictionary<string, object?>
            {
                ["type"] = BuildTypeValue(parameter.Kind, parameter.Nullable)
            };

            if (!string.IsNullOrWhiteSpace(parameter.Description))
            {
                schema["description"] = parameter.Description;
            }

            if (parameter.EnumValues is { Count: > 0 })
            {
                schema["enum"] = parameter.EnumValues;
            }

            if (parameter.Kind == ToolValueKind.Object)
            {
                var children = parameter.Properties ?? new List<ToolParameter>();
                string[] requiredChildren = BuildRequiredList(children);

                schema["properties"] = children.ToDictionary(
                    child => child.Name,
                    BuildParameterSchema);

                if (requiredChildren.Length > 0)
                {
                    schema["required"] = requiredChildren;
                }

                schema["additionalProperties"] = false;
            }

            if (parameter.Kind == ToolValueKind.Array && parameter.Items is not null)
            {
                schema["items"] = BuildParameterSchema(parameter.Items);
            }

            return schema;
        }

        private static object BuildTypeValue(ToolValueKind kind, bool nullable)
        {
            string jsonType = kind switch
            {
                ToolValueKind.String => "string",
                ToolValueKind.Integer => "integer",
                ToolValueKind.Number => "number",
                ToolValueKind.Boolean => "boolean",
                ToolValueKind.Object => "object",
                ToolValueKind.Array => "array",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };

            return nullable ? new[] { jsonType, "null" } : jsonType;
        }

        private static string[] BuildRequiredList(IEnumerable<ToolParameter> parameters)
        {
            return parameters
                .Select(parameter => parameter.Name)
                .ToArray();
        }
    }

    public sealed class OpenAiResponsesCreateRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; set; }

        [JsonPropertyName("previous_response_id")]
        public string? PreviousResponseId { get; set; }

        [JsonPropertyName("instructions")]
        public required string Instructions { get; set; }

        [JsonPropertyName("input")]
        public List<object>? Input { get; set; }

        [JsonPropertyName("tools")]
        public List<object>? Tools { get; set; }

        [JsonPropertyName("prompt_cache_key")]
        public required string PromptCacheKey { get; set; }

        [JsonPropertyName("parallel_tool_calls")]
        public bool ParallelToolCalls { get; set; }

        [JsonPropertyName("service_tier")]
        public required string ServiceTier { get; set; }

        [JsonPropertyName("reasoning")]
        public required ReasoningConfig Reasoning { get; set; }

        [JsonPropertyName("text")]
        public TextConfig? Text { get; set; }
    }

    public sealed class ReasoningConfig
    {
        [JsonPropertyName("effort")]
        public required string Effort { get; set; }
    }

    public sealed class TextConfig
    {
        [JsonPropertyName("verbosity")]
        public required string Verbosity { get; set; }
    }

    public sealed class InputUserMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; set; }

        [JsonPropertyName("content")]
        public required List<InputContentItem> Content { get; set; }
    }

    public sealed class InputFunctionCallOutputItem
    {
        [JsonPropertyName("type")]
        public required string Type { get; set; }

        [JsonPropertyName("call_id")]
        public required string CallId { get; set; }

        [JsonPropertyName("output")]
        public required string Output { get; set; }
    }

    public sealed class InputContentItem
    {
        [JsonPropertyName("type")]
        public required string Type { get; set; }

        [JsonPropertyName("text")]
        public required string Text { get; set; }
    }
}
