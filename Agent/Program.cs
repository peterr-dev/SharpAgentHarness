using System.Text.Json.Serialization;
using Core;
using Tools;
using Hooks;

var builder = WebApplication.CreateBuilder(args);

// Accept enum values as strings in API JSON bodies, so the web UI can send readable values.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();
app.UseHttpsRedirection();
app.UseStaticFiles();

// Default session configuration.
const string DefaultModel = "gpt-5-nano";
const string DefaultInstructions = "You are a helpful assistant.";
const string DefaultPromptCacheKey = "SharpAgentHarness";
const ServiceTier DefaultTier = ServiceTier.Auto;
const ReasoningEffort DefaultReasoning = ReasoningEffort.Low;
const TextVerbosity DefaultVerbosity = TextVerbosity.Low;
const string DefaultToolkit = "Default";

// Create a toolkit and add custom tools to it.
var defaultToolkit = new Toolkit(DefaultToolkit);
defaultToolkit.Add(new GetCurrentTimeTool());
Toolkits.Add(defaultToolkit);

// Register custom hooks.
HookRegistry.Register(new EnsureCurrentTimeZoneHook());

app.MapGet("/api", () =>
{
    return Results.Ok("Hello from the SharpAgentHarness API!");
});

// Create a new session and return its ID
app.MapPost("/api/sessions", (CreateSessionRequest? body) =>
{
    try
    {
        Session session = new Session(
            model: body?.Model ?? DefaultModel,
            instructions: body?.Instructions ?? DefaultInstructions,
            promptCacheKey: body?.PromptCacheKey ?? DefaultPromptCacheKey,
            tier: body?.Tier ?? DefaultTier,
            reasoning: body?.Reasoning ?? DefaultReasoning,
            verbosity: body?.Verbosity ?? DefaultVerbosity,
            toolkit: Toolkits.Get(body?.Toolkit ?? DefaultToolkit));
        SessionRegistry.Add(session);
        return Results.Ok(session);
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

// Get session details
app.MapGet("/api/sessions/{sessionId}", (Guid sessionId) =>
{
    try
    {
        Session session = SessionRegistry.GetSession(sessionId);
        return Results.Ok(session);
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


// Get all recorded events for a session
app.MapGet("/api/sessions/{sessionId}/events", (Guid sessionId) =>
{
    try
    {
        // Ensure the session exists before returning events.
        SessionRegistry.GetSession(sessionId);

        IReadOnlyList<ISessionEvent> events = EventTraces.GetEventsForSession(sessionId);

        // Return a consistent, explicit event shape so the response is easy to consume.
        List<EventResponseItem> eventResponse = events
            .Select(evt => evt switch
            {
                TurnStarted turnStarted => new EventResponseItem(
                    EventType: nameof(TurnStarted),
                    SessionId: turnStarted.Session.Id,
                    Details: new { }),
                LlmRequestReady requestSent => new EventResponseItem(
                    EventType: nameof(LlmRequestReady),
                    SessionId: requestSent.Session.Id,
                    Details: new
                    {
                        request = requestSent.req
                    }),
                LlmResponseReceived responseReceived => new EventResponseItem(
                    EventType: nameof(LlmResponseReceived),
                    SessionId: responseReceived.Session.Id,
                    Details: new
                    {
                        response = responseReceived.resp
                    }),
                RawLlmRequestReady rawRequest => new EventResponseItem(
                    EventType: nameof(RawLlmRequestReady),
                    SessionId: rawRequest.Session.Id,
                    Details: new
                    {
                        requestBody = rawRequest.requestBody
                    }),
                ToolCallRequested toolCallRequested => new EventResponseItem(
                    EventType: nameof(ToolCallRequested),
                    SessionId: toolCallRequested.Session.Id,
                    Details: new
                    {
                        toolCall = toolCallRequested.toolCall
                    }),
                ToolCallCompleted toolCallCompleted => new EventResponseItem(
                    EventType: nameof(ToolCallCompleted),
                    SessionId: toolCallCompleted.Session.Id,
                    Details: new
                    {
                        toolCall = toolCallCompleted.toolCall,
                        resultText = toolCallCompleted.resultText
                    }),
                TurnCompleted turnCompleted => new EventResponseItem(
                    EventType: nameof(TurnCompleted),
                    SessionId: turnCompleted.Session.Id,
                    Details: new { }),
                _ => throw new Exception($"Unhandled event type: {evt.GetType().Name}")
            })
            .ToList();

        return Results.Ok(eventResponse);
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

// Send a message within a session
app.MapPost("/api/sessions/{sessionId}/messages", async (Guid sessionId, SendMessageRequest body) =>
{
    try
    {
        string response = await Session.HandleMessageAsync(sessionId, body.message);
        return Results.Ok(new { response });
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

record CreateSessionRequest(
    string? Model,
    string? Instructions,
    string? PromptCacheKey,
    ServiceTier? Tier,
    ReasoningEffort? Reasoning,
    TextVerbosity? Verbosity,
    string? Toolkit);

record SendMessageRequest(string message);

record EventResponseItem(string EventType, Guid SessionId, object Details);
