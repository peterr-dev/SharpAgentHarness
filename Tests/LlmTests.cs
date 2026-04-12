using Agent;
using Agent.Llm;
using Agent.Tools;
using System.Text.Json;
using Xunit.Abstractions;

namespace Tests;

public class LlmTests
{
    private readonly ITestOutputHelper _output;
    private const string DefaultModel = "gpt-5-nano";
    private const string DefaultPromptCacheKey = "Test";
    private const ReasoningEffort DefaultReasoning = ReasoningEffort.Low;
    private const TextVerbosity DefaultVerbosity = TextVerbosity.Low;

    public LlmTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // A verbose system prompt used to seed the conversation with enough tokens that
    // OpenAI's prompt cache threshold (~1024 tokens) is crossed after a few turns,
    // at which point CachedInputTokens should become non-zero.
    private const string LargeSystemPrompt =
        "You are a knowledgeable and helpful assistant designed to support software engineers " +
        "working on agent-based systems that interact with large language models. " +
        "Your role is to answer questions clearly, explain technical concepts in depth, " +
        "and provide code examples when appropriate. " +
        "You understand the OpenAI Responses API, including features such as multi-turn " +
        "conversations using previous_response_id, prompt caching via cached input tokens, " +
        "reasoning models, tool calling, function calling, streaming, and structured output. " +
        "When answering, always consider accuracy first, then brevity. " +
        "Prefer concrete examples over abstract explanations. " +
        "When discussing token counts, caching behaviour, or API parameters, be precise. " +
        "You are also familiar with C# and .NET, particularly ASP.NET Core, and can help " +
        "debug and review code written in those frameworks. " +
        "You follow best practices for secure coding, clean architecture, and testability. " +
        "You know about dependency injection, async/await patterns, HTTP client usage, " +
        "JSON serialisation with System.Text.Json, xUnit testing, and integration testing. " +
        "You are patient, thorough, and always willing to revisit a topic if the user " +
        "needs further clarification. Do not make up facts; if you are unsure, say so. " +
        "You are aware that prompt caches on the OpenAI platform are maintained per " +
        "organisation and are automatically invalidated after a period of inactivity. " +
        "Cached tokens appear in the input_tokens_details.cached_tokens field of the " +
        "API response and represent input tokens that were served from the prompt cache " +
        "rather than being processed afresh, which reduces latency and cost. " +
        "Caching is automatic and requires no special configuration beyond using a " +
        "consistent prompt prefix that exceeds the minimum cache threshold of 1024 tokens. " +
        "When chaining conversation turns with previous_response_id, the server stores " +
        "the full exchange from the referenced response and prepends it to the new input, " +
        "allowing the conversation history to grow without the client managing it manually. " +
        "This means each subsequent turn only needs to send the new user message, and the " +
        "total input_tokens reported in the response will include all prior context. " +
        "As the conversation grows, earlier parts of the context that remain unchanged " +
        "across turns become eligible for caching, and you should expect to see " +
        "cached_tokens increase as the conversation deepens beyond the cache threshold. " +
        "Always respond concisely to each user message in this test conversation.";

    private sealed record RawRequestSnapshot(
        string? Model,
        string? PromptCacheKey,
        string? Instructions,
        string? ToolsSignature,
        string? ServiceTier,
        string? ReasoningEffort,
        bool? ParallelToolCalls,
        string? TextVerbosity,
        string? PreviousResponseId);

    private void AssertRawRequestsLookCacheable(Session session, IReadOnlyList<string> responseIds)
    {
        var rawRequestEvents = EventTraces.GetEventsForSession<LlmRawRequestSent>(session);

        Assert.NotEmpty(rawRequestEvents);
        Assert.True(
            rawRequestEvents.Count == responseIds.Count,
            $"Expected {responseIds.Count} raw request events, but found {rawRequestEvents.Count}.");

        List<RawRequestSnapshot> snapshots = rawRequestEvents
            .Select(rawRequestEvent => ParseRawRequestSnapshot(rawRequestEvent.RequestBody))
            .ToList();

        var baseline = snapshots[0];

        for (int index = 0; index < snapshots.Count; index++)
        {
            var snapshot = snapshots[index];
            int turnNumber = index + 1;

            _output.WriteLine(
                $"Raw request {turnNumber} diagnostics — model: {FormatDiagnosticValue(snapshot.Model)}, " +
                $"prompt_cache_key: {FormatDiagnosticValue(snapshot.PromptCacheKey)}, " +
                $"service_tier: {FormatDiagnosticValue(snapshot.ServiceTier)}, " +
                $"reasoning.effort: {FormatDiagnosticValue(snapshot.ReasoningEffort)}, " +
                $"previous_response_id: {FormatDiagnosticValue(snapshot.PreviousResponseId)}, " +
                $"tools: {FormatDiagnosticValue(snapshot.ToolsSignature)}");

            Assert.True(snapshot.Model == baseline.Model, $"Turn {turnNumber}: model changed between requests.");
            Assert.True(snapshot.PromptCacheKey == baseline.PromptCacheKey, $"Turn {turnNumber}: prompt_cache_key changed between requests.");
            Assert.True(snapshot.Instructions == baseline.Instructions, $"Turn {turnNumber}: instructions changed between requests.");
            Assert.True(snapshot.ToolsSignature == baseline.ToolsSignature, $"Turn {turnNumber}: tools changed between requests.");
            Assert.True(snapshot.ServiceTier == baseline.ServiceTier, $"Turn {turnNumber}: service_tier changed between requests.");
            Assert.True(snapshot.ReasoningEffort == baseline.ReasoningEffort, $"Turn {turnNumber}: reasoning.effort changed between requests.");
            Assert.True(snapshot.ParallelToolCalls == baseline.ParallelToolCalls, $"Turn {turnNumber}: parallel_tool_calls changed between requests.");
            Assert.True(snapshot.TextVerbosity == baseline.TextVerbosity, $"Turn {turnNumber}: text.verbosity changed between requests.");
        }

        Assert.True(
            snapshots[0].PreviousResponseId is null,
            "Turn 1: previous_response_id should be omitted on the first request.");

        for (int index = 1; index < snapshots.Count; index++)
        {
            string expectedPreviousResponseId = responseIds[index - 1];
            string? actualPreviousResponseId = snapshots[index].PreviousResponseId;

            Assert.True(
                actualPreviousResponseId == expectedPreviousResponseId,
                $"Turn {index + 1}: previous_response_id did not match the prior response id.");
        }
    }

    private static RawRequestSnapshot ParseRawRequestSnapshot(string requestBody)
    {
        using JsonDocument document = JsonDocument.Parse(requestBody);
        JsonElement root = document.RootElement;

        string? reasoningEffort = null;
        if (root.TryGetProperty("reasoning", out JsonElement reasoningElement))
        {
            reasoningEffort = GetOptionalString(reasoningElement, "effort");
        }

        string? textVerbosity = null;
        if (root.TryGetProperty("text", out JsonElement textElement))
        {
            textVerbosity = GetOptionalString(textElement, "verbosity");
        }

        return new RawRequestSnapshot(
            Model: GetOptionalString(root, "model"),
            PromptCacheKey: GetOptionalString(root, "prompt_cache_key"),
            Instructions: GetOptionalString(root, "instructions"),
            ToolsSignature: BuildToolsSignature(root),
            ServiceTier: GetOptionalString(root, "service_tier"),
            ReasoningEffort: reasoningEffort,
            ParallelToolCalls: GetOptionalBoolean(root, "parallel_tool_calls"),
            TextVerbosity: textVerbosity,
            PreviousResponseId: GetOptionalString(root, "previous_response_id"));
    }

    private static string? BuildToolsSignature(JsonElement root)
    {
        if (!root.TryGetProperty("tools", out JsonElement toolsElement) || toolsElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (toolsElement.ValueKind != JsonValueKind.Array)
        {
            return toolsElement.GetRawText();
        }

        return string.Join(
            "||",
            toolsElement
                .EnumerateArray()
                .Select(toolElement =>
                {
                    string toolName = GetOptionalString(toolElement, "name") ?? string.Empty;
                    return $"{toolName}:{toolElement.GetRawText()}";
                })
                .OrderBy(toolDefinition => toolDefinition, StringComparer.Ordinal));
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement propertyValue) || propertyValue.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return propertyValue.ValueKind == JsonValueKind.String
            ? propertyValue.GetString()
            : propertyValue.GetRawText();
    }

    private static bool? GetOptionalBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement propertyValue) || propertyValue.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return propertyValue.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string FormatDiagnosticValue(string? value)
    {
        if (value is null)
        {
            return "<null>";
        }

        return value.Length <= 120 ? value : $"{value[..120]}...";
    }

    /// <summary>
    /// Sends a single request to the agent and verifies that a response is returned
    /// with at least one output item
    /// </summary>
    [Fact]
    public async Task ModelRespondsToHello()
    {
        LlmClient llm = new LlmClient();
        var toolkit = new Toolkit("integration_test");

        Session session = new Session(
            model: DefaultModel,
            instructions: "You are a helpful assistant.",
            promptCacheKey: DefaultPromptCacheKey,
            tier: ServiceTier.Default,
            reasoning: DefaultReasoning,
            verbosity: DefaultVerbosity,
            toolkit: toolkit);

        Request request = new Request
        {
            Model = DefaultModel,
            PromptCacheKey = DefaultPromptCacheKey,
            Reasoning = DefaultReasoning,
            Instructions = "You are a helpful assistant.",
            InputMessage = new EasyInputMessage
            {
                Content = "Hi, who are you?"
            },
            Tier = ServiceTier.Default
        };

        _output.WriteLine($"User: {(request.InputMessage as EasyInputMessage)?.Content}");

        // Act
        Response response = await llm.SendMessageAsync(session, request);

        // Assert — expect a successful response with at least one output item
        Assert.NotNull(response);
        var successResponse = Assert.IsType<SuccessResponse>(response);
        Assert.NotEmpty(successResponse.Output);

        // Log the first text content from the response
        var firstText = successResponse.Output
            .OfType<ResponseOutputItemMessage>()
            .SelectMany(m => m.Content.OfType<ResponseContentPartText>())
            .FirstOrDefault()?.Text;
        _output.WriteLine($"Assistant: {firstText}");
    }

    /// <summary>
    /// Asks the model what the current time is, which should prompt it to invoke
    /// the get_current_time tool rather than answering from its training data
    /// </summary>
    [Fact]
    public async Task ModelRequestsToolCall()
    {
        LlmClient llm = new LlmClient();
        var toolkit = new Toolkit("integration_test");
        toolkit.Add(new GetCurrentTimeTool());

        Session session = new Session(
            model: DefaultModel,
            instructions: "You are a helpful assistant.",
            promptCacheKey: DefaultPromptCacheKey,
            tier: ServiceTier.Default,
            reasoning: DefaultReasoning,
            verbosity: DefaultVerbosity,
            toolkit: toolkit);

        Request request = new Request
        {
            Model = DefaultModel,
            PromptCacheKey = DefaultPromptCacheKey,
            Reasoning = DefaultReasoning,
            Instructions = "You are a helpful assistant.",
            Toolkit = toolkit,
            InputMessage = new EasyInputMessage
            {
                Content = "What is the current time in UTC?"
            },
            Tier = ServiceTier.Default
        };

        _output.WriteLine($"User: {(request.InputMessage as EasyInputMessage)?.Content}");

        Response response = await llm.SendMessageAsync(session, request);

        // Assert — expect the model to have requested the get_current_time tool
        Assert.NotNull(response);
        var successResponse = Assert.IsType<SuccessResponse>(response);
        Assert.NotEmpty(successResponse.Output);

        var toolCall = successResponse.Output
            .OfType<ResponseOutputItemFunctionCall>()
            .FirstOrDefault();

        Assert.NotNull(toolCall);
        Assert.Equal("get_current_time", toolCall.Name);

        _output.WriteLine($"Tool called: {toolCall.Name}");
        _output.WriteLine($"Arguments: {toolCall.Arguments}");
    }

    /// <summary>
    /// Verifies the model can continue after a tool call by receiving the tool result
    /// on the next turn and then responding with the returned time.
    /// </summary>
    [Fact]
    public async Task ModelRequestsToolCallAndProcessesResult()
    {
        // Arrange
        LlmClient llm = new LlmClient();
        var toolkit = new Toolkit("integration_test");
        toolkit.Add(new GetCurrentTimeTool());

        Session session = new Session(
            model: DefaultModel,
            instructions: "You are a helpful assistant.",
            promptCacheKey: DefaultPromptCacheKey,
            tier: ServiceTier.Default,
            reasoning: DefaultReasoning,
            verbosity: DefaultVerbosity,
            toolkit: toolkit);

        var req1 = new Request
        {
            Model = DefaultModel,
            PromptCacheKey = DefaultPromptCacheKey,
            Reasoning = DefaultReasoning,
            Instructions = "You are a helpful assistant. When a tool returns the current time, reply with exactly the returned timestamp and nothing else.",
            Toolkit = toolkit,
            InputMessage = new EasyInputMessage
            {
                Content = "What is the current time in UTC?"
            },
            Tier = ServiceTier.Default
        };

        _output.WriteLine($"User: {(req1.InputMessage as EasyInputMessage)?.Content}");

        // Act - first turn should request the tool
        Response firstResponse = await llm.SendMessageAsync(session, req1);

        // Assert - tool call requested
        var firstSuccess = Assert.IsType<SuccessResponse>(firstResponse);
        var toolCall = firstSuccess.Output
            .OfType<ResponseOutputItemFunctionCall>()
            .FirstOrDefault();

        Assert.NotNull(toolCall);
        Assert.Equal("get_current_time", toolCall.Name);
        Assert.False(string.IsNullOrWhiteSpace(toolCall.CallId));

        var toolResult = await new GetCurrentTimeTool().ExecuteAsync(toolCall.Arguments ?? "{}");
        _output.WriteLine($"Tool called: {toolCall.Name}");
        _output.WriteLine($"Arguments: {toolCall.Arguments}");
        _output.WriteLine($"Tool result: {toolResult}");

        var req2 = new Request
        {
            Model = DefaultModel,
            PromptCacheKey = DefaultPromptCacheKey,
            Reasoning = DefaultReasoning,
            Verbosity = DefaultVerbosity,
            PreviousResponseId = firstSuccess.Id,
            Instructions = "You are a helpful assistant. When a tool returns the current time, reply with exactly the returned timestamp and nothing else.",
            Toolkit = toolkit,
            InputMessage = new FunctionCallOutputMessage
            {
                CallId = toolCall.CallId!,
                Output = toolResult
            },
            Tier = ServiceTier.Default
        };

        Response resp2 = await llm.SendMessageAsync(session, req2);

        // Assert - assistant answers with the tool result
        var secondSuccess = Assert.IsType<SuccessResponse>(resp2);
        Assert.NotEmpty(secondSuccess.Output);

        var assistantText = secondSuccess.Output
            .OfType<ResponseOutputItemMessage>()
            .SelectMany(message => message.Content.OfType<ResponseContentPartText>())
            .FirstOrDefault()?.Text;

        Assert.Equal(toolResult, assistantText?.Trim());

        _output.WriteLine($"Assistant: {assistantText}");
    }

    /// <summary>
    /// Performs a multi-turn conversation, using previous_response_id to chain each turn,
    /// continuing until the response reports CachedInputTokens > 0 or we each 2000 input tokens
    /// without caching appearing. This verifies that prompt caching is working as expected.
    /// The large system prompt seeds enough tokens so that the conversation context
    /// crosses the cache threshold (~1024 tokens) within a handful of turns
    /// </summary>
    [Fact]
    public async Task MultiTurnConversationEventuallySeesCachedTokens()
    {
        LlmClient llm = new LlmClient();
        var toolkit = new Toolkit("integration_test");

        string? previousResponseId = null;
        const int maxTurns = 15;
        bool sawCachedTokens = false;
        bool exceededInputTokenBudget = false;
        int lastObservedInputTokens = 0;
        List<string> responseIds = new();
        string nextMessage = "Hello! Could you briefly introduce yourself and summarise what you can help me with?";

        // Follow-up messages to keep the conversation growing across turns
        string[] followUpMessages =
        [
            "Can you explain how previous_response_id works in more detail?",
            "How does prompt caching reduce latency on the OpenAI platform?",
            "What is the minimum number of tokens required to trigger the prompt cache?",
            "In C#, how would I extract the cached token count from an API response?",
            "Can you show a simple example of chaining two API calls with previous_response_id?",
            "What happens to the cache if I change the system prompt between turns?",
            "How do I verify that caching is working when I call the Responses API?",
            "Are cached tokens charged at a reduced rate? Explain the pricing model.",
            "How does the cache interact with reasoning models, such as o-series models?",
            "What other techniques exist for reducing token costs across multi-turn conversations?",
            "Summarise everything we have discussed so far in three bullet points.",
            "Thank you — is there anything else useful I should know about the Responses API?",
            "One final question: what are the retention limits for stored responses?",
            "Got it. Please confirm you have answered all my questions."
        ];

        Session session = new Session(
            model: DefaultModel,
            instructions: LargeSystemPrompt,
            promptCacheKey: DefaultPromptCacheKey,
            tier: ServiceTier.Default,
            reasoning: DefaultReasoning,
            verbosity: DefaultVerbosity,
            toolkit: toolkit);

        for (int turn = 0; turn < maxTurns; turn++)
        {
            Request request = new Request
            {
                Model = DefaultModel,
                PreviousResponseId = previousResponseId,
                PromptCacheKey = DefaultPromptCacheKey,
                Reasoning = DefaultReasoning,
                Tier = ServiceTier.Default,
                Instructions = LargeSystemPrompt,
                Toolkit = toolkit,
                InputMessage = new EasyInputMessage
                {
                    Content = nextMessage
                }
            };

            _output.WriteLine($"--- Turn {turn + 1} ---");
            _output.WriteLine($"User: {(request.InputMessage as EasyInputMessage)?.Content}");

            Response response = await llm.SendMessageAsync(session, request);
            var success = Assert.IsType<SuccessResponse>(response);
            Assert.False(string.IsNullOrWhiteSpace(success.Id));
            responseIds.Add(success.Id!);

            int cachedTokens = success.Usage?.CachedInputTokens ?? 0;
            int inputTokens = success.Usage?.InputTokens ?? 0;
            int outputTokens = success.Usage?.OutputTokens ?? 0;
            lastObservedInputTokens = inputTokens;

            // Log the first text content and usage details for this turn
            var firstText = success.Output
                .OfType<ResponseOutputItemMessage>()
                .SelectMany(m => m.Content.OfType<ResponseContentPartText>())
                .FirstOrDefault()?.Text;
            _output.WriteLine($"Assistant: {firstText}");
            _output.WriteLine($"Usage — input: {inputTokens}, cached: {cachedTokens}, output: {outputTokens}");

            // Once caching is observed, the test has achieved its goal
            if (cachedTokens > 0)
            {
                sawCachedTokens = true;
                break;
            }

            // Stop early if the cumulative input has grown well past the ~1200 token
            // target without caching appearing — this avoids burning excessive tokens
            // while still giving the cache time to warm up
            if (inputTokens >= 2000)
            {
                exceededInputTokenBudget = true;
                break;
            }

            // Prepare for the next turn
            int followUpIndex = turn < followUpMessages.Length ? turn : followUpMessages.Length - 1;
            nextMessage = followUpMessages[followUpIndex];
            previousResponseId = success.Id!;
        }

        if (!sawCachedTokens)
        {
            _output.WriteLine("Cached tokens were not observed; inspecting raw request events for cache-sensitive fields.");
            AssertRawRequestsLookCacheable(session, responseIds);

            Assert.False(
                exceededInputTokenBudget,
                $"Requests look cacheable but CachedInputTokens remained 0.");
        }

        Assert.True(sawCachedTokens, "Expected to observe CachedInputTokens > 0 before the conversation context reached ~2000 tokens.");
    }

    /// <summary>
    /// Verifies that cancelling the supplied CancellationToken whilst RunTurnAsync
    /// is blocked awaiting the LLM causes an OperationCanceledException to propagate.
    /// Passes an already-cancelled token so that HttpClient throws immediately,
    /// without requiring a network connection.
    /// </summary>
    [Fact]
    public async Task TurnCanBeCancelled()
    {
        // Arrange
        var turn = new Turn(maxIterations: 1);
        var toolkit = new Toolkit("integration_test");

        Session session = new Session(
            model: DefaultModel,
            instructions: "You are a helpful assistant.",
            promptCacheKey: DefaultPromptCacheKey,
            tier: ServiceTier.Default,
            reasoning: DefaultReasoning,
            verbosity: DefaultVerbosity,
            toolkit: toolkit);

        // Cancel the token before the call so HttpClient throws immediately
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert — the already-cancelled token should propagate as OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => turn.RunTurnAsync(session, "Hello", cts.Token));
    }
}
