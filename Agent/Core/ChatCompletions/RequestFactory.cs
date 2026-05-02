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

    public sealed class GptOssRequestOptions
    {
        public GptOssReasoningEffort? ReasoningEffort { get; init; }
    }

    public sealed class QwenRequestOptions
    {
        public bool? EnableThinking { get; init; }
    }

    public static class RequestFactory
    {
        public static Request Create(
            RequestModel requestModel,
            List<ChatCompletionMessageParam> messages,
            List<ChatCompletionTool>? tools,
            OpenAiRequestOptions? openAi,
            GptOssRequestOptions? gptOss,
            QwenRequestOptions? qwen)
        {
            Request request = requestModel switch
            {
                RequestModel.OpenAi => new OpenAiRequest
                {
                    Model = openAi?.ModelName,
                    ReasoningEffort = openAi?.ReasoningEffort,
                    Verbosity = openAi?.Verbosity,
                    ServiceTier = openAi?.ServiceTier,
                    PromptCacheKey = openAi?.PromptCacheKey
                },
                RequestModel.GptOss => new GptOssRequest
                {
                    ReasoningEffort = gptOss?.ReasoningEffort
                },
                RequestModel.Qwen36 => new QwenRequest
                {
                    EnableThinking = qwen?.EnableThinking
                },
                _ => throw new InvalidOperationException($"Unsupported model: {requestModel}")
            };

            request.Messages = messages;
            request.Tools = tools ?? new List<ChatCompletionTool>();
            return request;
        }
    }
}
