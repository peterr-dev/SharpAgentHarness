using Agent.Llm;
using Agent.Tools;

namespace Agent
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

        public async Task<string> RunTurnAsync(Agent agent, string userMessage, CancellationToken cancellationToken = default)
        {
            if (agent is null) throw new ArgumentNullException(nameof(agent));
            if (string.IsNullOrWhiteSpace(userMessage)) throw new ArgumentNullException(nameof(userMessage));
            Message nextInputMessage = new EasyInputMessage { Content = userMessage };

            EventTraces.Publish(new TurnStarted(agent));

            for (var iteration = 0; iteration < _maxIterations; iteration++)
            {
                Request req = new Request
                {
                    Model = agent.Model,
                    Tier = agent.Tier,
                    PromptCacheKey = agent.PromptCacheKey,
                    PreviousResponseId = agent.PreviousResponseId,
                    Reasoning = agent.Reasoning,
                    Verbosity = agent.Verbosity,
                    Instructions = agent.Instructions,
                    Toolkit = agent.Toolkit,
                    InputMessage = nextInputMessage
                };

                EventTraces.Publish(new LlmRequestSent(agent, req));
                var response = await _llmClient.SendMessageAsync(agent, req, cancellationToken);

                if (response is ErrorResponse error)
                    throw new InvalidOperationException(error.Message ?? "The LLM request failed.");

                var successResponse = response as SuccessResponse
                    ?? throw new InvalidOperationException("The LLM returned an unknown response type.");
                agent.PreviousResponseId = successResponse.Id;
                agent.UsageTotals.Add(successResponse.Usage);

                List<ResponseOutputItemFunctionCall> toolCalls = successResponse.Output
                    .OfType<ResponseOutputItemFunctionCall>()
                    .Where(toolCall =>
                        !string.IsNullOrWhiteSpace(toolCall.CallId) &&
                        !string.IsNullOrWhiteSpace(toolCall.Name))
                    .ToList();
                EventTraces.Publish(new LlmResponseReceived(agent, response));

                if (toolCalls.Count == 0)
                {
                    string finalAnswer = string.Join(
                        Environment.NewLine,
                        successResponse.Output
                        .OfType<ResponseOutputItemMessage>()
                        .SelectMany(message => message.Content.OfType<ResponseContentPartText>())
                        .Select(content => content.Text)
                        .Where(text => !string.IsNullOrWhiteSpace(text)));
                    EventTraces.Publish(new TurnCompleted(agent));
                    return finalAnswer;
                }
                else if (toolCalls.Count > 1)
                {
                    throw new InvalidOperationException("Parallel tool calls are not supported in this version of the agent loop.");
                }
                else
                {
                    ResponseOutputItemFunctionCall toolCall = toolCalls.First();
                    EventTraces.Publish(new ToolCallRequested(agent, toolCall));

                    string toolResult;
                    try
                    {
                        toolResult = await agent.Toolkit.ExecuteAsync(
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
                    EventTraces.Publish(new ToolCallCompleted(agent, toolCall, toolResult));

                    nextInputMessage = new FunctionCallOutputMessage
                    {
                        CallId = toolCall.CallId!,
                        Output = toolResult
                    };
                }
            }

            throw new InvalidOperationException($"Agent loop exceeded max iterations ({_maxIterations}).");
        }
    }
}
