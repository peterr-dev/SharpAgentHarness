using Core.ChatCompletions;
using System.Text.Json;
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

        public required RequestModel RequestModel { get; init; }
        public required TurnOptions Options { get; init; }

        public async Task<ChatCompletionMessage> RunTurnAsync(ChatCompletionMessageParam message)
        {
            HookRegistry.RunTurnStartedHooks(Session);
            int turnStartIndex = Session.Messages.Count;

            try
            {
                for (var iteration = 0; iteration < MaxIterations; iteration++)
                {
                    Session.Messages.Add(message);

                    Request request = RequestFactory.Create(
                        RequestModel,
                        BuildRequestMessages(Session.Messages, turnStartIndex),
                        Toolkit?.Tools,
                        Options);

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
                            ValidateStructuredOutputFinalContent(Session, Options.StructuredOutput, choice.Message.Content);

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

                            Session.Messages.Add(new ChatCompletionAssistantMessageParam
                            {
                                Content = null,
                                ToolCalls = choice.Message.ToolCalls.ToList(),
                                ReasoningContent = choice.Message.ReasoningContent
                            });

                            foreach (ChatCompletionMessageToolCall toolCall in choice.Message.ToolCalls)
                            {
                                if (toolCall is ChatCompletionMessageFunctionCall functionCall)
                                {
                                    ChatCompletionFunctionTool? functionTool = Toolkit?.Tools.OfType<ChatCompletionFunctionTool>().FirstOrDefault(t => t.Name.Equals(functionCall.FunctionName, StringComparison.OrdinalIgnoreCase));
                                    if (functionTool is not null)
                                    {
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

        // Build the messages for the next request based on the Session; we clone messages as some need to be modified, specifically removing reasoning from prior turns
        private static List<ChatCompletionMessageParam> BuildRequestMessages(List<ChatCompletionMessageParam> sourceMessages, int turnStartIndex)
        {
            List<ChatCompletionMessageParam> messagesForNextRequest = new List<ChatCompletionMessageParam>(sourceMessages.Count);

            for (int index = 0; index < sourceMessages.Count; index++)
            {
                messagesForNextRequest.Add(CloneMessageForRequest(sourceMessages[index], index, turnStartIndex));
            }

            return messagesForNextRequest;
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
                // Omit reasoning from prior turns.
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

        private static void ValidateStructuredOutputFinalContent(Session session, StructuredOutputOptions? structuredOutput, string finalContent)
        {
            if (structuredOutput?.OutputMode is not string outputMode ||
                !string.Equals(outputMode, "json_schema", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            JsonDocument contentDocument;

            try
            {
                contentDocument = JsonDocument.Parse(finalContent);
            }
            catch (JsonException ex)
            {
                string jsonErrorPath = string.IsNullOrWhiteSpace(ex.Path) ? "$" : ex.Path;
                string jsonPreview = finalContent.Length <= 200 ? finalContent : $"{finalContent[..200]}...";
                Events.Publish(new StructuredOutputFinalContentInvalid(
                    session,
                    outputMode,
                    jsonErrorPath,
                    ex.Message,
                    jsonPreview));

                throw new ArgumentException($"Structured output parsing failed at path '{jsonErrorPath}': assistant final content is not valid JSON.");
            }

            if (structuredOutput.JsonSchema is null)
            {
                return;
            }

            if (TryValidateAgainstSchema(contentDocument.RootElement, structuredOutput.JsonSchema.Value, "$", out string errorPath, out string errorMessage))
            {
                return;
            }

            string preview = finalContent.Length <= 200 ? finalContent : $"{finalContent[..200]}...";

            Events.Publish(new StructuredOutputFinalContentInvalid(
                session,
                outputMode,
                errorPath,
                errorMessage,
                preview));

            throw new ArgumentException($"Structured output validation failed at path '{errorPath}': assistant final content does not conform to the configured JSON schema.");
        }

        // This validator intentionally focuses on common JSON Schema keywords used by the harness examples.
        private static bool TryValidateAgainstSchema(JsonElement instance, JsonElement schema, string path, out string errorPath, out string errorMessage)
        {
            if (schema.TryGetProperty("type", out JsonElement typeElement) &&
                typeElement.ValueKind == JsonValueKind.String)
            {
                string expectedType = typeElement.GetString()!;
                if (!MatchesType(instance, expectedType))
                {
                    errorPath = path;
                    errorMessage = $"Expected type '{expectedType}'.";
                    return false;
                }
            }

            if (schema.TryGetProperty("required", out JsonElement requiredElement) && instance.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonElement requiredNameElement in requiredElement.EnumerateArray())
                {
                    string requiredName = requiredNameElement.GetString()!;
                    if (!instance.TryGetProperty(requiredName, out _))
                    {
                        errorPath = path;
                        errorMessage = $"Missing required property '{requiredName}'.";
                        return false;
                    }
                }
            }

            if (schema.TryGetProperty("properties", out JsonElement propertiesElement) && instance.ValueKind == JsonValueKind.Object)
            {
                HashSet<string> allowedProperties = new(StringComparer.Ordinal);

                foreach (JsonProperty schemaProperty in propertiesElement.EnumerateObject())
                {
                    allowedProperties.Add(schemaProperty.Name);

                    if (instance.TryGetProperty(schemaProperty.Name, out JsonElement propertyInstance) &&
                        !TryValidateAgainstSchema(propertyInstance, schemaProperty.Value, $"{path}.{schemaProperty.Name}", out errorPath, out errorMessage))
                    {
                        return false;
                    }
                }

                if (schema.TryGetProperty("additionalProperties", out JsonElement additionalPropertiesElement) &&
                    additionalPropertiesElement.ValueKind == JsonValueKind.False)
                {
                    foreach (JsonProperty instanceProperty in instance.EnumerateObject())
                    {
                        if (!allowedProperties.Contains(instanceProperty.Name))
                        {
                            errorPath = $"{path}.{instanceProperty.Name}";
                            errorMessage = "Property is not allowed by the schema.";
                            return false;
                        }
                    }
                }
            }

            errorPath = path;
            errorMessage = string.Empty;
            return true;
        }

        private static bool MatchesType(JsonElement instance, string expectedType) => expectedType switch
        {
            "object" => instance.ValueKind == JsonValueKind.Object,
            "array" => instance.ValueKind == JsonValueKind.Array,
            "string" => instance.ValueKind == JsonValueKind.String,
            "number" => instance.ValueKind == JsonValueKind.Number,
            "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
            "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => instance.ValueKind == JsonValueKind.Null,
            _ => true
        };
    }
}
