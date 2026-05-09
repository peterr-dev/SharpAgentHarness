using System.Text.Json;

namespace Core.ChatCompletions
{
    public enum RequestModel
    {
        OpenAi,
        GptOss,
        Qwen36
    }

    public sealed class OpenAiRequestOptions
    {
        public string? ModelName { get; init; }
        public OpenAiReasoningEffort? ReasoningEffort { get; init; }
        public Verbosity? Verbosity { get; init; }
        public ServiceTier? ServiceTier { get; init; }
        public string? PromptCacheKey { get; init; }
    }

    public sealed class StructuredOutputOptions
    {
        public string? OutputMode { get; init; }
        public string? JsonSchemaName { get; init; }
        public JsonElement? JsonSchema { get; init; }
        public bool? JsonStrict { get; init; }
    }

    public sealed class GptOssRequestOptions
    {
        public required GptOssReasoningEffort ReasoningEffort { get; init; }
    }

    public sealed class QwenRequestOptions
    {
        public required bool EnableThinking { get; init; }
    }

    public sealed class TurnOptions
    {
        public StructuredOutputOptions? StructuredOutput { get; init; }
        public OpenAiRequestOptions? OpenAi { get; init; }
        public GptOssRequestOptions? GptOss { get; init; }
        public QwenRequestOptions? Qwen { get; init; }
    }

    public static class RequestFactory
    {
        public static Request Create(
            RequestModel requestModel,
            List<ChatCompletionMessageParam> messages,
            List<ChatCompletionTool>? tools,
            TurnOptions options)
        {
            Request request = requestModel switch
            {
                RequestModel.OpenAi => new OpenAiRequest
                {
                    Model = options.OpenAi?.ModelName,
                    ReasoningEffort = options.OpenAi?.ReasoningEffort,
                    Verbosity = options.OpenAi?.Verbosity,
                    ServiceTier = options.OpenAi?.ServiceTier,
                    PromptCacheKey = options.OpenAi?.PromptCacheKey
                },
                RequestModel.GptOss => new GptOssRequest
                {
                    ReasoningEffort = options.GptOss?.ReasoningEffort
                        ?? throw new ArgumentException("GptOss reasoningEffort is required when using the GptOss request model.", nameof(options))
                },
                RequestModel.Qwen36 => new QwenRequest
                {
                    EnableThinking = options.Qwen?.EnableThinking
                        ?? throw new ArgumentException("Qwen enableThinking is required when using the Qwen36 request model.", nameof(options))
                },
                _ => throw new InvalidOperationException($"Unsupported model: {requestModel}")
            };

            request.Messages = messages;
            request.Tools = tools ?? new List<ChatCompletionTool>();
            request.StructuredOutput = options.StructuredOutput;
            return request;
        }
    }
}
