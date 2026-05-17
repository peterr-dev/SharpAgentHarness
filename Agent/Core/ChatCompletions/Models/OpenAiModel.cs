using System.Text.Json.Nodes;

namespace Core.ChatCompletions.Models
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

    internal sealed class OpenAiModel : IChatModel
    {
        public RequestModel Kind => RequestModel.OpenAi;

        public Uri ChatCompletionsUri => new("https://api.openai.com/v1/chat/completions");

        public bool IncludePriorTurnReasoning => false;

        public Request CreateRequest(
            IReadOnlyList<ChatCompletionMessageParam> messages,
            List<ChatCompletionTool>? tools,
            StructuredOutputOptions? structuredOutput,
            TurnOptions options)
        {
            return new OpenAiRequest
            {
                Model = options.OpenAi?.ModelName,
                ReasoningEffort = options.OpenAi?.ReasoningEffort,
                Verbosity = options.OpenAi?.Verbosity,
                ServiceTier = options.OpenAi?.ServiceTier,
                PromptCacheKey = options.OpenAi?.PromptCacheKey,
                Messages = messages.ToList(),
                Tools = tools ?? new List<ChatCompletionTool>(),
                StructuredOutput = structuredOutput
            };
        }
    }
}
