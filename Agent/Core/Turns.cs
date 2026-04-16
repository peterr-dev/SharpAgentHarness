using Core.Llm;

namespace Core
{
    public class Turn
    {
        private readonly LlmClient _llmClient;

        private readonly int _maxIterations;

        public Turn(int maxIterations)
        {
            _llmClient = new LlmClient();
            _maxIterations = maxIterations;
        }

        public async Task<string> RunTurnAsync(Session session, string userMessage, CancellationToken cancellationToken = default)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(userMessage)) throw new ArgumentNullException(nameof(userMessage));
            Message nextInputMessage = new EasyInputMessage { Content = userMessage };

            EventTraces.Publish(new TurnStarted(session));

            for (var iteration = 0; iteration < _maxIterations; iteration++)
            {
                Request req = new Request
                {
                    Model = session.Model,
                    Tier = session.Tier,
                    PromptCacheKey = session.PromptCacheKey,
                    PreviousResponseId = session.PreviousResponseId,
                    Reasoning = session.Reasoning,
                    Verbosity = session.Verbosity,
                    Instructions = session.Instructions,
                    Toolkit = session.Toolkit,
                    InputMessage = nextInputMessage
                };

                EventTraces.Publish(new LlmRequestSent(session, req));
                var response = await _llmClient.SendMessageAsync(session, req, cancellationToken);

                if (response is ErrorResponse error)
                    throw new InvalidOperationException(error.Message ?? "The LLM request failed.");

                var successResponse = response as SuccessResponse
                    ?? throw new InvalidOperationException("The LLM returned an unknown response type.");
                session.PreviousResponseId = successResponse.Id;
                session.UsageTotals.Add(successResponse.Usage);

                List<ResponseOutputItemFunctionCall> toolCalls = successResponse.Output
                    .OfType<ResponseOutputItemFunctionCall>()
                    .Where(toolCall =>
                        !string.IsNullOrWhiteSpace(toolCall.CallId) &&
                        !string.IsNullOrWhiteSpace(toolCall.Name))
                    .ToList();
                EventTraces.Publish(new LlmResponseReceived(session, response));

                if (toolCalls.Count == 0)
                {
                    string finalAnswer = string.Join(
                        Environment.NewLine,
                        successResponse.Output
                        .OfType<ResponseOutputItemMessage>()
                        .SelectMany(message => message.Content.OfType<ResponseContentPartText>())
                        .Select(content => content.Text)
                        .Where(text => !string.IsNullOrWhiteSpace(text)));
                    EventTraces.Publish(new TurnCompleted(session));
                    return finalAnswer;
                }
                else if (toolCalls.Count > 1)
                {
                    throw new InvalidOperationException("Parallel tool calls are not supported.");
                }
                else
                {
                    ResponseOutputItemFunctionCall toolCall = toolCalls.First();
                    EventTraces.Publish(new ToolCallRequested(session, toolCall));

                    string toolResult;
                    try
                    {
                        toolResult = await session.Toolkit.ExecuteAsync(
                            toolCall.Name!,
                            toolCall.Arguments ?? "{}");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        toolResult = $"Tool error: {ex.Message}";
                    }
                    EventTraces.Publish(new ToolCallCompleted(session, toolCall, toolResult));

                    nextInputMessage = new FunctionCallOutputMessage
                    {
                        CallId = toolCall.CallId!,
                        Output = toolResult
                    };
                }
            }

            throw new InvalidOperationException($"Harness loop exceeded max iterations ({_maxIterations}).");
        }
    }
}