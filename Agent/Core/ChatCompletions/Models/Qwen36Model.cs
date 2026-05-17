using System.Text.Json.Nodes;

namespace Core.ChatCompletions.Models
{
    public sealed class QwenRequest : Request
    {
        public required bool EnableThinking { get; set; }

        protected override void AddModelSpecificFields(JsonObject body)
        {
            body["chat_template_kwargs"] = new JsonObject
            {
                ["enable_thinking"] = EnableThinking
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
