using System.Text.Json.Nodes;

namespace Core.ChatCompletions.Models
{
    public sealed class GptOssRequest : Request
    {
        public required GptOssReasoningEffort ReasoningEffort { get; set; }

        protected override void AddModelSpecificFields(JsonObject body)
        {
            body["chat_template_kwargs"] = new JsonObject
            {
                ["reasoning_effort"] = ReasoningEffort.ToString().ToLowerInvariant()
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
}
