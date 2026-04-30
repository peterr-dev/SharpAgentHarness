using Core.ChatCompletions;
using System.Threading;

namespace Core
{
    public class Turn
    {
        public required Session Session { get; init; }

        public required ApiClient ApiClient { get; init; }

        public required Uri ChatCompletionsUri { get; init; }

        public required CancellationToken CancellationToken { get; init; }

        public required int MaxIterations { get; init; }

        public Toolkit? Toolkit { get; init; }

        public double? Temperature { get; init; }

        public string? Model { get; init; }

        public ReasoningEffort? ReasoningEffort { get; init; }

        public Verbosity? Verbosity { get; init; }

        public ServiceTier? ServiceTier { get; init; }

        public string? PromptCacheKey { get; init; }

        public async Task<ChatCompletionMessage> RunTurnAsync(ChatCompletionMessageParam message)
        {
            HookRegistry.RunTurnStartedHooks(Session);
            int turnStartIndex = Session.Messages.Count;

            try
            {
                for (var iteration = 0; iteration < MaxIterations; iteration++)
                {
                    Session.Messages.Add(message);

                    Request request = new Request
                    {
                        Messages = ProjectRequestMessages(Session.Messages, turnStartIndex)
                    };

                    if (Toolkit != null)
                        request.Tools = Toolkit.Tools;

                    if (Temperature != null)
                        request.Temperature = Temperature;

                    if (Model != null)
                        request.Model = Model;

                    if (PromptCacheKey != null)
                        request.PromptCacheKey = PromptCacheKey;

                    if (ReasoningEffort != null)
                        request.ReasoningEffort = ReasoningEffort;

                    if (Verbosity != null)
                        request.Verbosity = Verbosity;

                    if (ServiceTier != null)
                        request.ServiceTier = ServiceTier;

                    HookRegistry.RunRequestReadyHooks(Session, request);

                    Response response = await ApiClient.SendMessageAsync(Session, request, ChatCompletionsUri, CancellationToken);
                    HookRegistry.RunResponseReceivedHooks(Session, response);

                    if (response is SuccessResponse success)
                    {
                        Session.AddUsage(success.Usage);
                        ChatCompletionChoice choice = success.Choices.FirstOrDefault() ?? throw new InvalidOperationException("LLM response does not contain any choices.");

                        if (choice.FinishReason == FinishReason.Stop)
                        {
                            if (string.IsNullOrEmpty(choice.Message.Content)) throw new InvalidOperationException("LLM response does not contain content.");

                            Session.Messages.Add(new ChatCompletionAssistantMessageParam
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
                                    ChatCompletionFunctionTool? functionTool = Toolkit?.Tools.OfType<ChatCompletionFunctionTool>().FirstOrDefault(t => t.Name.Equals(functionCall.FunctionName, StringComparison.OrdinalIgnoreCase));
                                    if (functionTool is not null)
                                    {
                                        Session.Messages.Add(new ChatCompletionAssistantMessageParam
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

                throw new InvalidOperationException($"Maximum of {MaxIterations} iterations reached without a 'stop' finish reason from the LLM.");
            }
            finally
            {
                HookRegistry.RunTurnCompletedHooks(Session);
            }
        }

        private static List<ChatCompletionMessageParam> ProjectRequestMessages(List<ChatCompletionMessageParam> sourceMessages, int turnStartIndex)
        {
            List<ChatCompletionMessageParam> projectedMessages = new List<ChatCompletionMessageParam>(sourceMessages.Count);

            for (int index = 0; index < sourceMessages.Count; index++)
            {
                projectedMessages.Add(CloneMessageForRequest(sourceMessages[index], index, turnStartIndex));
            }

            return projectedMessages;
        }

        private static ChatCompletionMessageParam CloneMessageForRequest(ChatCompletionMessageParam message, int index, int turnStartIndex)
        {
            return message switch
            {
                ChatCompletionDeveloperMessageParam developerMessage => new ChatCompletionDeveloperMessageParam
                {
                    Content = developerMessage.Content
                },
                ChatCompletionUserMessageParam userMessage => new ChatCompletionUserMessageParam
                {
                    Content = userMessage.Content.ToList()
                },
                ChatCompletionToolMessageParam toolMessage => new ChatCompletionToolMessageParam
                {
                    ToolCallId = toolMessage.ToolCallId,
                    Content = toolMessage.Content
                },
                ChatCompletionAssistantMessageParam assistantMessage => CreateAssistantRequestMessage(assistantMessage, index, turnStartIndex),
                _ => throw new InvalidOperationException($"Unsupported message type: {message.GetType().Name}")
            };
        }

        private static ChatCompletionAssistantMessageParam CreateAssistantRequestMessage(ChatCompletionAssistantMessageParam assistantMessage, int index, int turnStartIndex)
        {
            List<ChatCompletionMessageToolCall>? toolCalls = assistantMessage.ToolCalls?.Select(CloneToolCall).ToList();
            List<ChatCompletionContentPart>? content = assistantMessage.Content?.ToList();

            if (index < turnStartIndex)
            {
                return new ChatCompletionAssistantMessageParam
                {
                    Content = content,
                    ToolCalls = toolCalls
                };
            }

            return new ChatCompletionAssistantMessageParam
            {
                Content = content,
                ToolCalls = toolCalls,
                ReasoningContent = assistantMessage.ReasoningContent
            };
        }

        private static ChatCompletionMessageToolCall CloneToolCall(ChatCompletionMessageToolCall toolCall)
        {
            return toolCall switch
            {
                ChatCompletionMessageFunctionCall functionCall => new ChatCompletionMessageFunctionCall
                {
                    Id = functionCall.Id,
                    FunctionName = functionCall.FunctionName,
                    Arguments = functionCall.Arguments
                },
                _ => throw new InvalidOperationException($"Unsupported tool call type: {toolCall.GetType().Name}")
            };
        }
    }
}
