using Core;
using Core.ChatCompletions;

namespace Tests;

public class HarnessTests
{
    [Fact]
    public async Task SingleTurnSession()
    {
        // Arrange
        await using FakeApiClientServer server = await FakeApiClientServer.StartAsync();

        Session session = new Session
        {
            ChatCompletionsUrl = server.ChatCompletionsUri,
            Model = "gpt-5-nano",
            PromptCacheKey = "tests",
            ReasoningEffort = ReasoningEffort.Minimal,
            Verbosity = Verbosity.Low,
            ServiceTier = ServiceTier.Default
        };

        Request request = session.CreateRequest(
        [
            new ChatCompletionUserMessageParam
            {
                Content =
                [
                    new ChatCompletionContentPartText
                    {
                        Text = "Ping"
                    }
                ]
            }
        ]);

        ApiClient client = new ApiClient(server.Client);

        // Act
        Response response = await client.SendMessageAsync(session, request, CancellationToken.None);

        // Assert
        SuccessResponse success = Assert.IsType<SuccessResponse>(response);
        ChatCompletionChoice choice = Assert.Single(success.Choices);
        Assert.Equal(FinishReason.Stop, choice.FinishReason);
        Assert.Equal("Hello from fake local server.", choice.Message.Content);
        Assert.Equal(12, success.Usage.InputTokens);
        Assert.Equal(5, success.Usage.CachedInputTokens);
        Assert.Equal(7, success.Usage.OutputTokens);
        Assert.Equal(3, success.Usage.ReasoningOutputTokens);
    }

    [Fact]
    public async Task MultiTurnSessionWithToolUsage()
    {
        // Arrange
        const string fixedUtcNow = "2026-04-20T12:34:56.0000000+00:00";
        StaticGetCurrentTimeTool staticTimeTool = new StaticGetCurrentTimeTool(fixedUtcNow);

        const string expectedRequest1Body = """{"messages":[{"role":"developer","content":"You are a concise test assistant."},{"role":"user","content":[{"type":"text","text":"Hi"}]}],"tools":[{"type":"function","function":{"name":"get_current_time","description":"Get the current time in ISO 8601 format for a specified timezone.","strict":true,"parameters":{"type":"object","properties":{"timezone":{"type":"string","description":"The IANA timezone identifier (e.g., \u0027America/New_York\u0027). If not provided, defaults to UTC."}},"required":["timezone"],"additionalProperties":false}}}]}""";
        const string expectedRequest2Body = """{"messages":[{"role":"developer","content":"You are a concise test assistant."},{"role":"user","content":[{"type":"text","text":"Hi"}]},{"role":"assistant","content":[{"type":"text","text":"Hello!"}]},{"role":"user","content":[{"type":"text","text":"What is the current time in UTC?"}]}],"tools":[{"type":"function","function":{"name":"get_current_time","description":"Get the current time in ISO 8601 format for a specified timezone.","strict":true,"parameters":{"type":"object","properties":{"timezone":{"type":"string","description":"The IANA timezone identifier (e.g., \u0027America/New_York\u0027). If not provided, defaults to UTC."}},"required":["timezone"],"additionalProperties":false}}}]}""";
        const string expectedRequest3Body = """{"messages":[{"role":"developer","content":"You are a concise test assistant."},{"role":"user","content":[{"type":"text","text":"Hi"}]},{"role":"assistant","content":[{"type":"text","text":"Hello!"}]},{"role":"user","content":[{"type":"text","text":"What is the current time in UTC?"}]},{"role":"assistant","content":null,"tool_calls":[{"id":"call_utc_1","type":"function","function":{"name":"get_current_time","arguments":"{\u0022timezone\u0022:\u0022UTC\u0022}"}}]},{"role":"tool","tool_call_id":"call_utc_1","content":"2026-04-20T12:34:56.0000000\u002B00:00"}],"tools":[{"type":"function","function":{"name":"get_current_time","description":"Get the current time in ISO 8601 format for a specified timezone.","strict":true,"parameters":{"type":"object","properties":{"timezone":{"type":"string","description":"The IANA timezone identifier (e.g., \u0027America/New_York\u0027). If not provided, defaults to UTC."}},"required":["timezone"],"additionalProperties":false}}}]}""";
        const string expectedRequest4Body = """{"messages":[{"role":"developer","content":"You are a concise test assistant."},{"role":"user","content":[{"type":"text","text":"Hi"}]},{"role":"assistant","content":[{"type":"text","text":"Hello!"}]},{"role":"user","content":[{"type":"text","text":"What is the current time in UTC?"}]},{"role":"assistant","content":null,"tool_calls":[{"id":"call_utc_1","type":"function","function":{"name":"get_current_time","arguments":"{\u0022timezone\u0022:\u0022UTC\u0022}"}}]},{"role":"tool","tool_call_id":"call_utc_1","content":"2026-04-20T12:34:56.0000000\u002B00:00"},{"role":"assistant","content":[{"type":"text","text":"The current UTC time is 2026-04-20T12:34:56.0000000\u002B00:00."}]},{"role":"user","content":[{"type":"text","text":"Thanks"}]}],"tools":[{"type":"function","function":{"name":"get_current_time","description":"Get the current time in ISO 8601 format for a specified timezone.","strict":true,"parameters":{"type":"object","properties":{"timezone":{"type":"string","description":"The IANA timezone identifier (e.g., \u0027America/New_York\u0027). If not provided, defaults to UTC."}},"required":["timezone"],"additionalProperties":false}}}]}""";
        const string response1Body = """{"id":"chatcmpl_test_multiturn_1","object":"chat.completion","created":1710001001,"model":"gpt-5-nano","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"Hello!","refusal":""}}],"usage":{"prompt_tokens":20,"completion_tokens":4,"total_tokens":24,"prompt_tokens_details":{"cached_tokens":2},"completion_tokens_details":{"reasoning_tokens":1}}}""";
        const string response2Body = """{"id":"chatcmpl_test_multiturn_2","object":"chat.completion","created":1710001002,"model":"gpt-5-nano","choices":[{"index":0,"finish_reason":"tool_calls","message":{"role":"assistant","content":null,"refusal":"","tool_calls":[{"id":"call_utc_1","type":"function","function":{"name":"get_current_time","arguments":"{\"timezone\":\"UTC\"}"}}]}}],"usage":{"prompt_tokens":34,"completion_tokens":9,"total_tokens":43,"prompt_tokens_details":{"cached_tokens":3},"completion_tokens_details":{"reasoning_tokens":2}}}""";
        const string response3Body = """{"id":"chatcmpl_test_multiturn_3","object":"chat.completion","created":1710001003,"model":"gpt-5-nano","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"The current UTC time is 2026-04-20T12:34:56.0000000+00:00.","refusal":""}}],"usage":{"prompt_tokens":46,"completion_tokens":12,"total_tokens":58,"prompt_tokens_details":{"cached_tokens":4},"completion_tokens_details":{"reasoning_tokens":3}}}""";
        const string response4Body = """{"id":"chatcmpl_test_multiturn_4","object":"chat.completion","created":1710001004,"model":"gpt-5-nano","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"You are welcome.","refusal":""}}],"usage":{"prompt_tokens":52,"completion_tokens":5,"total_tokens":57,"prompt_tokens_details":{"cached_tokens":5},"completion_tokens_details":{"reasoning_tokens":1}}}""";

            Session requestSession = new Session
            {
                ChatCompletionsUrl = new Uri("http://localhost/v1/chat/completions"),
                Model = "gpt-5-nano",
                PromptCacheKey = "tests-e2e-multiturn",
                ReasoningEffort = ReasoningEffort.Minimal,
                Verbosity = Verbosity.Low,
                ServiceTier = ServiceTier.Default,
                Toolkit = new Toolkit("tests-request-toolkit")
            };
            requestSession.Toolkit.Add(staticTimeTool);

            Request request1 = requestSession.CreateRequest(
            [
                new ChatCompletionDeveloperMessageParam 
                { 
                    UseDeveloperMessageInsteadOfSystem = true,
                    Content = "You are a concise test assistant."
                },
                new ChatCompletionUserMessageParam { Content = [new ChatCompletionContentPartText { Text = "Hi" }] }
            ]);
            Request request2 = requestSession.CreateRequest(
            [
                new ChatCompletionDeveloperMessageParam 
                { 
                    UseDeveloperMessageInsteadOfSystem = true,
                    Content = "You are a concise test assistant."
                },
                new ChatCompletionUserMessageParam { Content = [new ChatCompletionContentPartText { Text = "Hi" }] },
                new ChatCompletionAssistantMessageParam { Content = [new ChatCompletionContentPartText { Text = "Hello!" }] },
                new ChatCompletionUserMessageParam { Content = [new ChatCompletionContentPartText { Text = "What is the current time in UTC?" }] }
            ]);
            Request request3 = requestSession.CreateRequest(
            [
                new ChatCompletionDeveloperMessageParam 
                { 
                    UseDeveloperMessageInsteadOfSystem = true,
                    Content = "You are a concise test assistant."
                },
                new ChatCompletionUserMessageParam { Content = [new ChatCompletionContentPartText { Text = "Hi" }] },
                new ChatCompletionAssistantMessageParam { Content = [new ChatCompletionContentPartText { Text = "Hello!" }] },
                new ChatCompletionUserMessageParam { Content = [new ChatCompletionContentPartText { Text = "What is the current time in UTC?" }] },
                new ChatCompletionAssistantMessageParam
                {
                    Content = null,
                    ToolCalls =
                    [
                        new ChatCompletionMessageFunctionCall
                        {
                            Id = "call_utc_1",
                            FunctionName = "get_current_time",
                            Arguments = "{\"timezone\":\"UTC\"}"
                        }
                    ]
                },
                new ChatCompletionToolMessageParam
                {
                    ToolCallId = "call_utc_1",
                    Content = "2026-04-20T12:34:56.0000000+00:00"
                }
            ]);
            Request request4 = requestSession.CreateRequest(
            [
                new ChatCompletionDeveloperMessageParam 
                { 
                    UseDeveloperMessageInsteadOfSystem = true,
                    Content = "You are a concise test assistant."
                },
                new ChatCompletionUserMessageParam { Content = [new ChatCompletionContentPartText { Text = "Hi" }] },
                new ChatCompletionAssistantMessageParam { Content = [new ChatCompletionContentPartText { Text = "Hello!" }] },
                new ChatCompletionUserMessageParam { Content = [new ChatCompletionContentPartText { Text = "What is the current time in UTC?" }] },
                new ChatCompletionAssistantMessageParam
                {
                    Content = null,
                    ToolCalls =
                    [
                        new ChatCompletionMessageFunctionCall
                        {
                            Id = "call_utc_1",
                            FunctionName = "get_current_time",
                            Arguments = "{\"timezone\":\"UTC\"}"
                        }
                    ]
                },
                new ChatCompletionToolMessageParam
                {
                    ToolCallId = "call_utc_1",
                    Content = "2026-04-20T12:34:56.0000000+00:00"
                },
                new ChatCompletionAssistantMessageParam
                {
                    Content = [new ChatCompletionContentPartText { Text = "The current UTC time is 2026-04-20T12:34:56.0000000+00:00." }]
                },
                new ChatCompletionUserMessageParam { Content = [new ChatCompletionContentPartText { Text = "Thanks" }] }
            ]);

            await using FakeApiClientServer server = await FakeApiClientServer.StartAsync(
                new Dictionary<string, string>
                {
                    [expectedRequest1Body] = response1Body,
                    [expectedRequest2Body] = response2Body,
                    [expectedRequest3Body] = response3Body,
                    [expectedRequest4Body] = response4Body
                });

            ApiClient fakeApiClient = new ApiClient(server.Client);
            Toolkit toolkit = new Toolkit("tests-e2e-tools");
            toolkit.Add(staticTimeTool);

            Session session = new Session(fakeApiClient)
            {
                ChatCompletionsUrl = server.ChatCompletionsUri,
                Model = "gpt-5-nano",
                PromptCacheKey = "tests-e2e-multiturn",
                ReasoningEffort = ReasoningEffort.Minimal,
                Verbosity = Verbosity.Low,
                ServiceTier = ServiceTier.Default,
                Toolkit = toolkit
            };
            session.AddMessage(new ChatCompletionDeveloperMessageParam { UseDeveloperMessageInsteadOfSystem = true, Content = "You are a concise test assistant." });
            Sessions.CreateSession(session);

            ChatCompletionUserMessageParam hiMessage = new ChatCompletionUserMessageParam
            {
                Content = [new ChatCompletionContentPartText { Text = "Hi" }]
            };
            ChatCompletionUserMessageParam askTimeMessage = new ChatCompletionUserMessageParam
            {
                Content = [new ChatCompletionContentPartText { Text = "What is the current time in UTC?" }]
            };
            ChatCompletionUserMessageParam thanksMessage = new ChatCompletionUserMessageParam
            {
                Content = [new ChatCompletionContentPartText { Text = "Thanks" }]
            };

            // Act
            ChatCompletionMessage hiResponse = await session.RunTurnAsync(hiMessage, CancellationToken.None);
            ChatCompletionMessage timeResponse = await session.RunTurnAsync(askTimeMessage, CancellationToken.None);
            ChatCompletionMessage thanksResponse = await session.RunTurnAsync(thanksMessage, CancellationToken.None);

            IReadOnlyList<RawRequestReady> rawRequests = Events.GetEventsForSession<RawRequestReady>(session.Id);
            IReadOnlyList<ResponseReceived> responseEvents = Events.GetEventsForSession<ResponseReceived>(session.Id);

            // Assert: raw request JSON payloads from the event log.
            Assert.Collection(
                rawRequests,
                requestEvent => Assert.Equal(expectedRequest1Body, requestEvent.RawRequest),
                requestEvent => Assert.Equal(expectedRequest2Body, requestEvent.RawRequest),
                requestEvent => Assert.Equal(expectedRequest3Body, requestEvent.RawRequest),
                requestEvent => Assert.Equal(expectedRequest4Body, requestEvent.RawRequest));

            // Assert: request model serialisation still matches the gold-standard fixtures.
            Assert.Equal(expectedRequest1Body, request1.ToJson());
            Assert.Equal(expectedRequest2Body, request2.ToJson());
            Assert.Equal(expectedRequest3Body, request3.ToJson());
            Assert.Equal(expectedRequest4Body, request4.ToJson());

            // Assert: key properties over Core.ChatCompletions.Response objects.
            Assert.Collection(responseEvents, _ => { }, _ => { }, _ => { }, _ => { });

            SuccessResponse firstResponse = Assert.IsType<SuccessResponse>(responseEvents[0].Response);
            Assert.Equal("chatcmpl_test_multiturn_1", firstResponse.Id);
            Assert.Equal("gpt-5-nano", firstResponse.Model);
            ChatCompletionChoice firstChoice = Assert.Single(firstResponse.Choices);
            Assert.Equal(FinishReason.Stop, firstChoice.FinishReason);
            Assert.Equal("Hello!", firstChoice.Message.Content);

            SuccessResponse secondResponse = Assert.IsType<SuccessResponse>(responseEvents[1].Response);
            Assert.Equal("chatcmpl_test_multiturn_2", secondResponse.Id);
            ChatCompletionChoice secondChoice = Assert.Single(secondResponse.Choices);
            Assert.Equal(FinishReason.ToolCalls, secondChoice.FinishReason);
            ChatCompletionMessageFunctionCall toolCall = Assert.IsType<ChatCompletionMessageFunctionCall>(Assert.Single(secondChoice.Message.ToolCalls!));
            Assert.Equal("get_current_time", toolCall.FunctionName);
            Assert.Equal("{\"timezone\":\"UTC\"}", toolCall.Arguments);

            SuccessResponse thirdResponse = Assert.IsType<SuccessResponse>(responseEvents[2].Response);
            Assert.Equal("chatcmpl_test_multiturn_3", thirdResponse.Id);
            ChatCompletionChoice thirdChoice = Assert.Single(thirdResponse.Choices);
            Assert.Equal(FinishReason.Stop, thirdChoice.FinishReason);
            Assert.Equal("The current UTC time is 2026-04-20T12:34:56.0000000+00:00.", thirdChoice.Message.Content);

            SuccessResponse fourthResponse = Assert.IsType<SuccessResponse>(responseEvents[3].Response);
            Assert.Equal("chatcmpl_test_multiturn_4", fourthResponse.Id);
            ChatCompletionChoice fourthChoice = Assert.Single(fourthResponse.Choices);
            Assert.Equal(FinishReason.Stop, fourthChoice.FinishReason);
            Assert.Equal("You are welcome.", fourthChoice.Message.Content);

            Assert.Equal("Hello!", hiResponse.Content);
            Assert.Equal("The current UTC time is 2026-04-20T12:34:56.0000000+00:00.", timeResponse.Content);
            Assert.Equal("You are welcome.", thanksResponse.Content);
    }

     private sealed class StaticGetCurrentTimeTool : ChatCompletionFunctionTool
    {
        private readonly string _fixedIsoTime;

        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public StaticGetCurrentTimeTool(string fixedIsoTime)
        {
            _fixedIsoTime = fixedIsoTime;
            Name = "get_current_time";
            Description = "Get the current time in ISO 8601 format for a specified timezone.";
            Strict = true;
            Parameters.Add(new FunctionToolParameter
            {
                Name = "timezone",
                Description = "The IANA timezone identifier (e.g., 'America/New_York'). If not provided, defaults to UTC.",
                Type = FunctionToolCallParameterType.String
            });
        }

        public override async Task<string> ExecuteAsync(string argumentsJson)
        {
            await Task.CompletedTask;
            return _fixedIsoTime;
        }
    }
}
