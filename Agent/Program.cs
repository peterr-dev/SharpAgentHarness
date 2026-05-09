using Core;
using Core.ChatCompletions;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
TimeSpan? chatCompletionsTimeout = builder.Configuration.GetValue<TimeSpan?>("ChatCompletions:Timeout");
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Allow enum values in JSON request bodies to be passed as strings (e.g. "Auto").
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddHttpClient<ApiClient>(httpClient =>
{
    // Allow the chat completions HTTP timeout to be configured per environment.
    if (chatCompletionsTimeout is TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new InvalidOperationException("ChatCompletions:Timeout must be greater than zero.");

        httpClient.Timeout = timeout;
    }
});
var app = builder.Build();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.MapGet("/favicon.ico", () => Results.Redirect("/favicon.svg", permanent: false));

// Create a toolkit with an example function tool.
Toolkit exampleToolkit;
try
{
    exampleToolkit = Toolkits.Get("Example");
}
catch (KeyNotFoundException)
{
    exampleToolkit = new Toolkit("Example");
    exampleToolkit.Add(new GetCurrentTimeTool());
    Toolkits.Add(exampleToolkit);
}

app.MapGet("/api", () =>
{
    return Results.Ok("Hello from the SharpAgentHarness API!");
});

app.MapPost("/api/sessions", (CreateSessionRequest? body) =>
{
    try
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrEmpty(body.instructions, nameof(body.instructions));
        Session session = Sessions.CreateSession(body.instructions);
        return Results.Ok(MapSessionForApi(session));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/api/sessions/{sessionId}", (Guid sessionId) =>
{
    try
    {
        Session session = Sessions.GetSession(sessionId);
        return Results.Ok(MapSessionForApi(session));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/api/sessions/{sessionId}/events", (Guid sessionId) =>
{
    try
    {
        Session session = Sessions.GetSession(sessionId);
        List<object> events = Events.GetEventsForSession(sessionId)
            .Select(MapEventForApi)
            .ToList();

        return Results.Ok(events);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/api/sessions/{sessionId}/messages", async (Guid sessionId, SendMessageRequest body, ApiClient apiClient) =>
{
    try
    {
        ArgumentException.ThrowIfNullOrEmpty(body.message, nameof(body.message));
        ValidateStructuredOutputRequest(body);

        Session session = Sessions.GetSession(sessionId);
        int maxIterations = body.maxIterations;
        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(body.maxIterations), "MaxIterations must be greater than zero.");

        Toolkit? toolkit = string.IsNullOrEmpty(body.toolkit)
            ? null
            : Toolkits.Get(body.toolkit);

        ChatCompletionUserMessageParam userMessage = new ChatCompletionUserMessageParam
        {
            Content = new List<ChatCompletionContentPart>
            {
                new ChatCompletionContentPartText { Text = body.message }
            }
        };

        Turn turn = new Turn
        {
            Session = session,
            Toolkit = toolkit,
            ApiClient = apiClient,
            ChatCompletionsUri = GetChatCompletionsUri(body.model),
            MaxIterations = maxIterations,
            RequestModel = body.model,
            Options = new TurnOptions
            {
                StructuredOutput = string.IsNullOrWhiteSpace(body.outputMode) ? null : new StructuredOutputOptions
                {
                    OutputMode = body.outputMode,
                    JsonSchemaName = body.jsonSchemaName,
                    JsonSchema = body.jsonSchema,
                    JsonStrict = body.jsonStrict
                },
                OpenAi = body.openAi is null ? null : new OpenAiRequestOptions
                {
                    ModelName = body.modelName,
                    PromptCacheKey = body.openAi.promptCacheKey,
                    ReasoningEffort = body.openAi.reasoningEffort,
                    Verbosity = body.openAi.verbosity,
                    ServiceTier = body.openAi.serviceTier
                },
                GptOss = body.gptOss is null ? null : new GptOssRequestOptions
                {
                    ReasoningEffort = body.gptOss.reasoningEffort
                },
                Qwen = body.qwen is null ? null : new QwenRequestOptions
                {
                    EnableThinking = body.qwen.enableThinking
                }
            },
            CancellationToken = CancellationToken.None
        };
        ChatCompletionMessage response = await turn.RunTurnAsync(userMessage);

        return Results.Ok(new { response.Content });
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.Run();


// Convert internal session state to a stable JSON shape for the web UI.
static object MapSessionForApi(Session session)
{
    ChatCompletionDeveloperMessageParam? instructionsMessage = session.Messages
        .OfType<ChatCompletionDeveloperMessageParam>()
        .FirstOrDefault();

    return new
    {
        id = session.Id,
        instructions = instructionsMessage?.Content,
        usageTotals = new
        {
            inputTokens = session.TotalInputTokens,
            cachedInputTokens = session.TotalCachedInputTokens,
            outputTokens = session.TotalOutputTokens,
            reasoningOutputTokens = session.TotalReasoningOutputTokens
        }
    };
}

// Convert internal event types to a stable JSON shape for the web UI.
static object MapEventForApi(Event evt)
{
    return evt switch
    {
        RequestReady requestReady => new
        {
            type = nameof(RequestReady),
            sessionId = requestReady.Session.Id,
            details = new
            {
                request = MapRequestForApi(requestReady.Request)
            }
        },
        ResponseReceived responseReceived => new
        {
            type = nameof(ResponseReceived),
            sessionId = responseReceived.Session.Id,
            details = new
            {
                response = MapResponseForApi(responseReceived.Response),
                session = MapSessionForApi(responseReceived.Session)
            }
        },
        RawRequestReady rawRequestReady => new
        {
            type = nameof(RawRequestReady),
            sessionId = rawRequestReady.Session.Id,
            details = new
            {
                rawRequest = rawRequestReady.RawRequest
            }
        },
        RawResponseReceived rawResponseReceived => new
        {
            type = nameof(RawResponseReceived),
            sessionId = rawResponseReceived.Session.Id,
            details = new
            {
                rawResponse = rawResponseReceived.RawResponse
            }
        },
        TurnStarted turnStarted => new
        {
            type = nameof(TurnStarted),
            sessionId = turnStarted.Session.Id
        },
        TurnCompleted turnCompleted => new
        {
            type = nameof(TurnCompleted),
            sessionId = turnCompleted.Session.Id
        },
        StructuredOutputFinalContentInvalid structuredOutputFinalContentInvalid => new
        {
            type = nameof(StructuredOutputFinalContentInvalid),
            sessionId = structuredOutputFinalContentInvalid.Session.Id,
            details = new
            {
                outputMode = structuredOutputFinalContentInvalid.OutputMode,
                errorPath = structuredOutputFinalContentInvalid.ErrorPath,
                errorMessage = structuredOutputFinalContentInvalid.ErrorMessage,
                contentPreview = structuredOutputFinalContentInvalid.ContentPreview
            }
        },
        _ => new
        {
            type = evt.GetType().Name,
            sessionId = evt.Session.Id
        }
    };
}

// Convert internal request type to the same JSON shape sent to chat completions.
static Uri GetChatCompletionsUri(RequestModel model)
{
    string uri = model switch
    {
        RequestModel.OpenAi => "https://api.openai.com/v1/chat/completions",
        RequestModel.GptOss => "http://localhost:8080/chat/completions",
        RequestModel.Qwen36 => "http://localhost:8080/chat/completions",
        _ => throw new InvalidOperationException($"Unsupported model: {model}")
    };

    return new Uri(uri);
}

static JsonElement MapRequestForApi(Request request)
{
    using JsonDocument document = JsonDocument.Parse(request.ToJson());
    return document.RootElement.Clone();
}

// Convert internal response type to a stable JSON shape for the web UI.
static object MapResponseForApi(Response response)
{
    return response switch
    {
        SuccessResponse success => new
        {
            type = nameof(SuccessResponse),
            id = success.Id,
            @object = success.Object,
            created = success.Created,
            model = success.Model,
            choices = success.Choices.Select(MapChoiceForApi).ToList(),
            usage = success.Usage
        },
        ErrorResponse error => new
        {
            type = nameof(ErrorResponse),
            message = error.Message,
            errorType = error.Type,
            param = error.Param,
            code = error.Code
        },
        _ => new
        {
            type = response.GetType().Name
        }
    };
}


static object MapChoiceForApi(ChatCompletionChoice choice)
{
    return new
    {
        index = choice.Index,
        finishReason = choice.FinishReason.ToString(),
        message = new
        {
            role = choice.Message.Role,
            content = choice.Message.Content,
            reasoningContent = choice.Message.ReasoningContent,
            refusal = choice.Message.Refusal,
            toolCalls = choice.Message.ToolCalls?.Select(MapToolCallForApi).ToList()
        }
    };
}

static object MapToolCallForApi(ChatCompletionMessageToolCall toolCall)
{
    return toolCall switch
    {
        ChatCompletionMessageFunctionCall functionCall => new
        {
            type = "function",
            id = functionCall.Id,
            name = functionCall.FunctionName,
            arguments = functionCall.Arguments
        },
        _ => new
        {
            type = toolCall.GetType().Name,
            id = toolCall.Id
        }
    };
}

static void ValidateStructuredOutputRequest(SendMessageRequest body)
{
    // Keep backward compatibility: if structured output fields are omitted, do not change behaviour.
    if (string.IsNullOrWhiteSpace(body.outputMode))
    {
        return;
    }

    if (!string.Equals(body.outputMode, "json_schema", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    if (body.jsonSchema is null)
    {
        throw new ArgumentException("Structured output requires jsonSchema when outputMode is 'json_schema'.");
    }

    try
    {
        JsonDocument.Parse(body.jsonSchema.Value.GetRawText());
    }
    catch (JsonException ex)
    {
        throw new ArgumentException($"jsonSchema must be valid JSON: {ex.Message}", ex);
    }
}

record CreateSessionRequest(string instructions);

record SendMessageRequest(
    string message,
    int maxIterations,
    string? toolkit,
    RequestModel model,
    string? modelName,
    string? outputMode,
    string? jsonSchemaName,
    JsonElement? jsonSchema,
    bool? jsonStrict,
    OpenAiOptions? openAi,
    GptOssOptions? gptOss,
    QwenOptions? qwen);

record OpenAiOptions(
    string? promptCacheKey,
    OpenAiReasoningEffort? reasoningEffort,
    Verbosity? verbosity,
    ServiceTier? serviceTier);

record GptOssOptions(
    GptOssReasoningEffort reasoningEffort);

record QwenOptions(
    bool enableThinking);

public partial class Program;
