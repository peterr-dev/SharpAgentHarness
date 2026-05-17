namespace Core.ChatCompletions.Models
{
    public static class ModelRegistry
    {
        private static readonly IChatModel OpenAi = new OpenAiModel();
        private static readonly IChatModel GptOss = new GptOssModel();
        private static readonly IChatModel Qwen36 = new Qwen36Model();

        public static IChatModel Get(RequestModel model)
        {
            return model switch
            {
                RequestModel.OpenAi => OpenAi,
                RequestModel.GptOss => GptOss,
                RequestModel.Qwen36 => Qwen36,
                _ => throw new InvalidOperationException($"Unsupported model: {model}")
            };
        }
    }

    internal sealed class GptOssModel : IChatModel
    {
        public RequestModel Kind => RequestModel.GptOss;

        public Uri ChatCompletionsUri => new("http://localhost:8080/v1/chat/completions");

        public bool IncludePriorTurnReasoning => false;

        public Request CreateRequest(
            IReadOnlyList<ChatCompletionMessageParam> messages,
            List<ChatCompletionTool>? tools,
            StructuredOutputOptions? structuredOutput,
            TurnOptions options)
        {
            return new GptOssRequest
            {
                ReasoningEffort = options.GptOss?.ReasoningEffort
                    ?? throw new ArgumentException("GptOss reasoningEffort is required when using the GptOss request model.", nameof(options)),
                Messages = messages.ToList(),
                Tools = tools ?? new List<ChatCompletionTool>(),
                StructuredOutput = structuredOutput
            };
        }
    }

    internal sealed class Qwen36Model : IChatModel
    {
        public RequestModel Kind => RequestModel.Qwen36;

        public Uri ChatCompletionsUri => new("http://localhost:8080/v1/chat/completions");

        public bool IncludePriorTurnReasoning => true;

        public Request CreateRequest(
            IReadOnlyList<ChatCompletionMessageParam> messages,
            List<ChatCompletionTool>? tools,
            StructuredOutputOptions? structuredOutput,
            TurnOptions options)
        {
            return new QwenRequest
            {
                EnableThinking = options.Qwen?.EnableThinking
                    ?? throw new ArgumentException("Qwen enableThinking is required when using the Qwen36 request model.", nameof(options)),
                Messages = messages.ToList(),
                Tools = tools ?? new List<ChatCompletionTool>(),
                StructuredOutput = structuredOutput
            };
        }
    }
}
