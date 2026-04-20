using Core.ChatCompletions;

namespace Core
{
    public class Turn
    {
        private readonly ApiClient _chatCompletionsClient;

        private readonly int _maxIterations;

        public Turn(ApiClient chatCompletionsClient, int maxIterations)
        {
            _chatCompletionsClient = chatCompletionsClient ?? throw new ArgumentNullException(nameof(chatCompletionsClient));
            _maxIterations = maxIterations;
        }

        public async Task<ChatCompletionMessage> RunTurnAsync(Session session, ChatCompletionMessageParam message, CancellationToken cancellationToken)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));
            if (message is null) throw new ArgumentNullException(nameof(message));

            await session.WaitForTurnAsync(cancellationToken);
            HookRegistry.RunTurnStartedHooks(session);

            try
            {
                for (var iteration = 0; iteration < _maxIterations; iteration++)
                {
                    session.AddMessage(message);
 
                    Request req = session.CreateRequest(session.Messages);
                    HookRegistry.RunRequestReadyHooks(session, req);
                    
                    Response response = await _chatCompletionsClient.SendMessageAsync(session, req, cancellationToken);
                    HookRegistry.RunResponseReceivedHooks(session, response);

                    if (response is SuccessResponse success)
                    {
                        session.AddUsage(success.Usage);
                        ChatCompletionChoice choice = success.Choices.FirstOrDefault() ?? throw new InvalidOperationException("LLM response does not contain any choices.");
                        
                        if (choice.FinishReason == FinishReason.Stop)
                        {
                            if (string.IsNullOrEmpty(choice.Message.Content)) throw new InvalidOperationException("LLM response does not contain content.");

                            session.AddMessage(new ChatCompletionAssistantMessageParam
                            {
                                Content = new List<ChatCompletionContentPart>
                                {
                                    new ChatCompletionContentPartText { Text = choice.Message.Content }
                                }
                            });

                            return choice.Message;
                        }
                        else if (choice.FinishReason == FinishReason.ToolCalls)
                        {
                            if (choice.Message.ToolCalls is null || choice.Message.ToolCalls.Count == 0)
                                throw new InvalidOperationException("LLM response indicated tool calls but did not contain any tool calls.");

                            foreach (ChatCompletionMessageToolCall toolCall in choice.Message.ToolCalls)
                            {
                                if (toolCall is ChatCompletionMessageFunctionCall functionCall)
                                {
                                    ChatCompletionFunctionTool? functionTool = session.Toolkit?.Tools.OfType<ChatCompletionFunctionTool>().FirstOrDefault(t => t.Name.Equals(functionCall.FunctionName, StringComparison.OrdinalIgnoreCase));
                                    if (functionTool is not null)
                                    {
                                        session.AddMessage(new ChatCompletionAssistantMessageParam
                                        {
                                            Content = null,
                                            ToolCalls = new List<ChatCompletionMessageToolCall> { functionCall }
                                        });

                                        string toolResponse = await functionTool.ExecuteAsync(functionCall.Arguments ?? string.Empty);

                                        ChatCompletionToolMessageParam toolCallResultsMessage = new ChatCompletionToolMessageParam
                                        {
                                            ToolCallId = functionCall.Id,
                                            Content = toolResponse
                                        };
                                        message = toolCallResultsMessage;
                                    }
                                    else
                                    {
                                        throw new InvalidOperationException($"LLM requested a function call to '{functionCall.FunctionName}' but no matching function tool was found in the toolkit.");
                                    }
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Unsupported tool call type: {toolCall.GetType().Name}");
                                }
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException($"LLM response returned with unsupported finish reason: {choice.FinishReason}");
                        }

                    }
                    else 
                    if (response is ErrorResponse error)
                    {
                        throw new InvalidOperationException($"The LLM returned an error response. Message: {error.Message}; Type: {error.Type}; Param: {error.Param}; Code: {error.Code}");
                    }
                }

                throw new InvalidOperationException($"Maximum of {_maxIterations} iterations reached without a 'stop' finish reason from the LLM.");
            }
            finally
            {
                HookRegistry.RunTurnCompletedHooks(session);
                session.ReleaseTurn();
            }
        }
    }
}
