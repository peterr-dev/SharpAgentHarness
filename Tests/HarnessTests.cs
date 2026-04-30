using Core;
using Core.ChatCompletions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tests;

public class HarnessTests
{
    [Fact]
    public async Task SingleTurnSession()
    {
        await using FakeApiClientServer server = await FakeApiClientServer.StartAsync();
        ApiClient apiClient = new ApiClient(server.Client);

        Session session = Sessions.CreateSession("You are a helpful assistant.", server.ChatCompletionsUri);

        ChatCompletionUserMessageParam userMessage = new ChatCompletionUserMessageParam
        {
            Content = new List<ChatCompletionContentPart>
            {
                new ChatCompletionContentPartText { Text = "Hello!" }
            }
        };

        Turn turn = new Turn
        {
            Session = session,
            ApiClient = apiClient,
            ChatCompletionsUri = session.ChatCompletionsUri,
            MaxIterations = 5,
            CancellationToken = CancellationToken.None
        };
        ChatCompletionMessage response = await turn.RunTurnAsync(userMessage);
        IReadOnlyList<ResponseReceived> responseEvents = Events.GetEventsForSession<ResponseReceived>(session.Id);

        // Assert
        SuccessResponse success = Assert.IsType<SuccessResponse>(Assert.Single(responseEvents).Response);
        ChatCompletionChoice choice = Assert.Single(success.Choices);
        Assert.Equal(FinishReason.Stop, choice.FinishReason);
        Assert.Equal("Hello from fake local server.", response.Content);
        Assert.Equal("Hello from fake local server.", choice.Message.Content);
        Assert.Equal(12, session.TotalInputTokens);
        Assert.Equal(5, session.TotalCachedInputTokens);
        Assert.Equal(7, session.TotalOutputTokens);
        Assert.Equal(3, session.TotalReasoningOutputTokens);
    }

    [Fact]
    public async Task MultiTurnSessionWithToolUsage()
    {
        // Arrange
        const string fixedUtcNow = "2026-04-20T12:34:56.0000000+00:00";
        StaticGetCurrentTimeTool staticTimeTool = new StaticGetCurrentTimeTool(fixedUtcNow);
        Toolkit toolkit = new Toolkit("tests-e2e-tools");
        toolkit.Add(staticTimeTool);

        const string expectedRequest1Body = """{"messages":[{"role":"developer","content":"You are a concise test assistant."},{"role":"user","content":[{"type":"text","text":"Hi"}]}],"tools":[{"type":"function","function":{"name":"get_current_time","description":"Get the current time in ISO 8601 format for a specified timezone.","strict":true,"parameters":{"type":"object","properties":{"timezone":{"type":"string","description":"The IANA timezone identifier (e.g., \u0027America/New_York\u0027). If not provided, defaults to UTC."}},"required":["timezone"],"additionalProperties":false}}}]}""";
        const string expectedRequest2Body = """{"messages":[{"role":"developer","content":"You are a concise test assistant."},{"role":"user","content":[{"type":"text","text":"Hi"}]},{"role":"assistant","content":[{"type":"text","text":"Hello!"}]},{"role":"user","content":[{"type":"text","text":"What is the current time in UTC?"}]}],"tools":[{"type":"function","function":{"name":"get_current_time","description":"Get the current time in ISO 8601 format for a specified timezone.","strict":true,"parameters":{"type":"object","properties":{"timezone":{"type":"string","description":"The IANA timezone identifier (e.g., \u0027America/New_York\u0027). If not provided, defaults to UTC."}},"required":["timezone"],"additionalProperties":false}}}]}""";
        const string expectedRequest3Body = """{"messages":[{"role":"developer","content":"You are a concise test assistant."},{"role":"user","content":[{"type":"text","text":"Hi"}]},{"role":"assistant","content":[{"type":"text","text":"Hello!"}]},{"role":"user","content":[{"type":"text","text":"What is the current time in UTC?"}]},{"role":"assistant","content":null,"tool_calls":[{"id":"call_utc_1","type":"function","function":{"name":"get_current_time","arguments":"{\u0022timezone\u0022:\u0022UTC\u0022}"}}],"reasoning_content":"thinking-about-time"},{"role":"tool","tool_call_id":"call_utc_1","content":"2026-04-20T12:34:56.0000000\u002B00:00"}],"tools":[{"type":"function","function":{"name":"get_current_time","description":"Get the current time in ISO 8601 format for a specified timezone.","strict":true,"parameters":{"type":"object","properties":{"timezone":{"type":"string","description":"The IANA timezone identifier (e.g., \u0027America/New_York\u0027). If not provided, defaults to UTC."}},"required":["timezone"],"additionalProperties":false}}}]}""";
        const string expectedRequest4Body = """{"messages":[{"role":"developer","content":"You are a concise test assistant."},{"role":"user","content":[{"type":"text","text":"Hi"}]},{"role":"assistant","content":[{"type":"text","text":"Hello!"}]},{"role":"user","content":[{"type":"text","text":"What is the current time in UTC?"}]},{"role":"assistant","content":null,"tool_calls":[{"id":"call_utc_1","type":"function","function":{"name":"get_current_time","arguments":"{\u0022timezone\u0022:\u0022UTC\u0022}"}}]},{"role":"tool","tool_call_id":"call_utc_1","content":"2026-04-20T12:34:56.0000000\u002B00:00"},{"role":"assistant","content":[{"type":"text","text":"The current UTC time is 2026-04-20T12:34:56.0000000\u002B00:00."}]},{"role":"user","content":[{"type":"text","text":"Thanks"}]}],"tools":[{"type":"function","function":{"name":"get_current_time","description":"Get the current time in ISO 8601 format for a specified timezone.","strict":true,"parameters":{"type":"object","properties":{"timezone":{"type":"string","description":"The IANA timezone identifier (e.g., \u0027America/New_York\u0027). If not provided, defaults to UTC."}},"required":["timezone"],"additionalProperties":false}}}]}""";
        const string response1Body = """{"id":"chatcmpl_test_multiturn_1","object":"chat.completion","created":1710001001,"model":"gpt-5-nano","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"Hello!","refusal":""}}],"usage":{"prompt_tokens":20,"completion_tokens":4,"total_tokens":24,"prompt_tokens_details":{"cached_tokens":2},"completion_tokens_details":{"reasoning_tokens":1}}}""";
        const string response2Body = """{"id":"chatcmpl_test_multiturn_2","object":"chat.completion","created":1710001002,"model":"gpt-5-nano","choices":[{"index":0,"finish_reason":"tool_calls","message":{"role":"assistant","content":null,"reasoning_content":"thinking-about-time","refusal":"","tool_calls":[{"id":"call_utc_1","type":"function","function":{"name":"get_current_time","arguments":"{\"timezone\":\"UTC\"}"}}]}}],"usage":{"prompt_tokens":34,"completion_tokens":9,"total_tokens":43,"prompt_tokens_details":{"cached_tokens":3},"completion_tokens_details":{"reasoning_tokens":2}}}""";
        const string response3Body = """{"id":"chatcmpl_test_multiturn_3","object":"chat.completion","created":1710001003,"model":"gpt-5-nano","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"The current UTC time is 2026-04-20T12:34:56.0000000+00:00.","refusal":""}}],"usage":{"prompt_tokens":46,"completion_tokens":12,"total_tokens":58,"prompt_tokens_details":{"cached_tokens":4},"completion_tokens_details":{"reasoning_tokens":3}}}""";
        const string response4Body = """{"id":"chatcmpl_test_multiturn_4","object":"chat.completion","created":1710001004,"model":"gpt-5-nano","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"You are welcome.","refusal":""}}],"usage":{"prompt_tokens":52,"completion_tokens":5,"total_tokens":57,"prompt_tokens_details":{"cached_tokens":5},"completion_tokens_details":{"reasoning_tokens":1}}}""";

        Request request1 = CreateExpectedRequest(
        [
            new ChatCompletionDeveloperMessageParam
            {
                Content = "You are a concise test assistant."
            },
            new ChatCompletionUserMessageParam { Content = [new ChatCompletionContentPartText { Text = "Hi" }] }
        ]);
        Request request2 = CreateExpectedRequest(
        [
            new ChatCompletionDeveloperMessageParam
            {
                Content = "You are a concise test assistant."
            },
            new ChatCompletionUserMessageParam { Content = [new ChatCompletionContentPartText { Text = "Hi" }] },
            new ChatCompletionAssistantMessageParam { Content = [new ChatCompletionContentPartText { Text = "Hello!" }] },
            new ChatCompletionUserMessageParam { Content = [new ChatCompletionContentPartText { Text = "What is the current time in UTC?" }] }
        ]);
        Request request3 = CreateExpectedRequest(
        [
            new ChatCompletionDeveloperMessageParam
            {
                Content = "You are a concise test assistant."
            },
            new ChatCompletionUserMessageParam { Content = [new ChatCompletionContentPartText { Text = "Hi" }] },
            new ChatCompletionAssistantMessageParam { Content = [new ChatCompletionContentPartText { Text = "Hello!" }] },
            new ChatCompletionUserMessageParam { Content = [new ChatCompletionContentPartText { Text = "What is the current time in UTC?" }] },
            new ChatCompletionAssistantMessageParam
            {
                Content = null,
                ReasoningContent = "thinking-about-time",
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
                Content = fixedUtcNow
            }
        ]);
        Request request4 = CreateExpectedRequest(
        [
            new ChatCompletionDeveloperMessageParam
            {
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
                Content = fixedUtcNow
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
        Session session = Sessions.CreateSession("You are a concise test assistant.", server.ChatCompletionsUri);

        Turn CreateTurn() => new Turn
        {
            Session = session,
            ApiClient = fakeApiClient,
            ChatCompletionsUri = session.ChatCompletionsUri,
            MaxIterations = 5,
            CancellationToken = CancellationToken.None,
            Toolkit = toolkit
        };

        Request CreateExpectedRequest(List<ChatCompletionMessageParam> messages) => new Request
        {
            Messages = messages,
            Tools = toolkit.Tools
        };

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
        ChatCompletionMessage hiResponse = await CreateTurn().RunTurnAsync(hiMessage);
        ChatCompletionMessage timeResponse = await CreateTurn().RunTurnAsync(askTimeMessage);
        ChatCompletionMessage thanksResponse = await CreateTurn().RunTurnAsync(thanksMessage);

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

    [Fact]
    public void AssistantToolCallsMessageSerialisesReasoningContentForMultipleToolCalls()
    {
        ChatCompletionAssistantMessageParam assistantMessage = new ChatCompletionAssistantMessageParam
        {
            Content = null,
            ReasoningContent = "multi-tool reasoning",
            ToolCalls =
            [
                new ChatCompletionMessageFunctionCall { Id = "call_1", FunctionName = "first_tool", Arguments = "{}" },
                new ChatCompletionMessageFunctionCall { Id = "call_2", FunctionName = "second_tool", Arguments = "{}" }
            ]
        };

        Assert.Equal("""{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"first_tool","arguments":"{}"}},{"id":"call_2","type":"function","function":{"name":"second_tool","arguments":"{}"}}],"reasoning_content":"multi-tool reasoning"}""", assistantMessage.ToJson().ToJsonString());
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
