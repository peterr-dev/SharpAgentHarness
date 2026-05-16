namespace Core.ChatCompletions.Models
{
    public interface IChatModel
    {
        RequestModel Kind { get; }

        Uri ChatCompletionsUri { get; }

        Request CreateRequest(
            IReadOnlyList<ChatCompletionMessageParam> messages,
            List<ChatCompletionTool>? tools,
            StructuredOutputOptions? structuredOutput,
            TurnOptions options);

        bool IncludePriorTurnReasoning { get; }
    }
}
