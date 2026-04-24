using Core;
using Core.ChatCompletions;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Allow enum values in JSON request bodies to be passed as strings (e.g. "Auto").
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
var app = builder.Build();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.MapGet("/favicon.ico", () => Results.Redirect("/favicon.svg", permanent: false));

// Default session configuration.
const string DefaultModel = "gpt-5-nano";
const string DefaultInstructions = "You are a helpful assistant.";
const string DefaultPromptCacheKey = "SharpAgentHarness";
const ServiceTier DefaultTier = ServiceTier.Default;
const ReasoningEffort DefaultReasoning = ReasoningEffort.Minimal;
const Verbosity DefaultVerbosity = Verbosity.Low;
const string DefaultToolkitName = "Default";

// Create a default toolkit with an example function tool.
Toolkit defaultToolkit = new Toolkit(DefaultToolkitName);
defaultToolkit.Add(new GetCurrentTimeTool());
Toolkits.Add(defaultToolkit);

app.MapGet("/api", () =>
{
    return Results.Ok("Hello from the SharpAgentHarness API!");
});

app.MapPost("/api/sessions", (CreateSessionRequest? body) =>
{
    try
    {
        Uri chatCompletionsUrl = ResolveChatCompletionsUrl(body?.ChatCompletionsUrl);

        Session session = new Session
        {
            ChatCompletionsUrl = chatCompletionsUrl,
            Model = body?.Model ?? DefaultModel,
            PromptCacheKey = body?.PromptCacheKey ?? DefaultPromptCacheKey,
            ServiceTier = body?.Tier ?? DefaultTier,
            ReasoningEffort = body?.Reasoning ?? DefaultReasoning,
            Verbosity = body?.Verbosity ?? DefaultVerbosity,
            Toolkit = string.IsNullOrEmpty(body?.ToolkitName) ? defaultToolkit : Toolkits.Get(body.ToolkitName)
        };
        session.AddMessage(new ChatCompletionDeveloperMessageParam 
        { 
            UseDeveloperMessageInsteadOfSystem = session.ChatCompletionsUrl.ToString() == ApiClient.OpenAIChatCompletionsUrl,
            Content = body?.Instructions ?? DefaultInstructions
        });
        Sessions.CreateSession(session);
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

app.MapPost("/api/sessions/{sessionId}/messages", async (Guid sessionId, SendMessageRequest body) =>
{
    try
    {
        Session session = Sessions.GetSession(sessionId);
        ChatCompletionUserMessageParam userMessage = new ChatCompletionUserMessageParam
        {
            Content = new List<ChatCompletionContentPart>
            {
                new ChatCompletionContentPartText { Text = body.message }
            }
        };
        ChatCompletionMessage response = await session.RunTurnAsync(userMessage, CancellationToken.None);

        return Results.Ok(new { response.Content });
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
        chatCompletionsUrl = session.ChatCompletionsUrl.ToString(),
        model = session.Model,
        promptCacheKey = session.PromptCacheKey,
        tier = session.ServiceTier,
        reasoning = session.ReasoningEffort,
        verbosity = session.Verbosity,
        instructions = instructionsMessage?.Content,
        toolkitName = session.Toolkit?.Name,
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
                request = requestReady.Request
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
        _ => new
        {
            type = evt.GetType().Name,
            sessionId = evt.Session.Id
        }
    };
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
            choices = success.Choices,
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

static Uri ResolveChatCompletionsUrl(string? requestedUrl)
{
    if (string.IsNullOrWhiteSpace(requestedUrl))
    {
        return new Uri(ApiClient.OpenAIChatCompletionsUrl);
    }

    if (Uri.TryCreate(requestedUrl, UriKind.Absolute, out Uri? chatCompletionsUrl))
    {
        return chatCompletionsUrl;
    }

    throw new ArgumentException("ChatCompletionsUrl must be a valid absolute URL.", nameof(requestedUrl));
}

record CreateSessionRequest(
    string? Model,
    string? Instructions,
    string? ChatCompletionsUrl,
    string? PromptCacheKey,
    ServiceTier? Tier,
    ReasoningEffort? Reasoning,
    Verbosity? Verbosity,
    string? ToolkitName
);

record SendMessageRequest(string message);

public partial class Program
{
}
