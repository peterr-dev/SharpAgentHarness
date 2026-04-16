using Agent.Tools;
using Agent.Llm;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Accept enum values as strings in API JSON bodies, so the web UI can send readable values.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();
app.UseHttpsRedirection();
app.UseStaticFiles();

// Default agent configuration
const string DefaultModel = "gpt-5-nano";
const string DefaultInstructions = "You are a helpful assistant.";
const string DefaultPromptCacheKey = "SharpAgentHarness";
const ServiceTier DefaultTier = ServiceTier.Auto;
const ReasoningEffort DefaultReasoning = ReasoningEffort.Low;
const TextVerbosity DefaultVerbosity = TextVerbosity.Low;
const string DefaultToolkit = "Default";

var defaultToolkit = new Toolkit(DefaultToolkit);
defaultToolkit.Add(new GetCurrentTimeTool());
Toolkits.Add(defaultToolkit);

app.MapGet("/api", () =>
{
    return Results.Ok("Hello from the SharpAgentHarness API!");
});

// Create a new agent and return its ID
app.MapPost("/api/agents", (CreateAgentRequest? body) =>
{
    try
    {
        Agent agent = new Agent(
            model: body?.Model ?? DefaultModel,
            instructions: body?.Instructions ?? DefaultInstructions,
            promptCacheKey: body?.PromptCacheKey ?? DefaultPromptCacheKey,
            tier: body?.Tier ?? DefaultTier,
            reasoning: body?.Reasoning ?? DefaultReasoning,
            verbosity: body?.Verbosity ?? DefaultVerbosity,
            toolkit: Toolkits.Get(body?.Toolkit ?? DefaultToolkit));
        Agents.Add(agent);
        return Results.Ok(agent);
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

// Get agent details
app.MapGet("/api/agents/{agentId}", (Guid agentId) =>
{
    try
    {
        Agent agent = Agents.GetAgent(agentId);
        return Results.Ok(agent);
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


// Get all recorded events for an agent
app.MapGet("/api/agents/{agentId}/events", (Guid agentId) =>
{
    try
    {
        // Ensure the agent exists before returning events.
        Agents.GetAgent(agentId);

        IReadOnlyList<IAgentEvent> events = EventTraces.GetEventsForAgent(agentId);

        // Return a consistent, explicit event shape so the response is easy to consume.
        List<EventResponseItem> eventResponse = events
            .Select(evt => evt switch
            {
                TurnStarted turnStarted => new EventResponseItem(
                    EventType: nameof(TurnStarted),
                    AgentId: turnStarted.Agent.Id,
                    Details: new
                    {
                        agent = turnStarted.Agent
                    }),
                LlmRawRequestSent rawRequest => new EventResponseItem(
                    EventType: nameof(LlmRawRequestSent),
                    AgentId: rawRequest.Agent.Id,
                    Details: new
                    {
                        requestBody = rawRequest.RequestBody
                    }),
                LlmRequestSent requestSent => new EventResponseItem(
                    EventType: nameof(LlmRequestSent),
                    AgentId: requestSent.Agent.Id,
                    Details: new
                    {
                        request = requestSent.req
                    }),
                LlmResponseReceived responseReceived => new EventResponseItem(
                    EventType: nameof(LlmResponseReceived),
                    AgentId: responseReceived.Agent.Id,
                    Details: new
                    {
                        response = responseReceived.resp
                    }),
                ToolCallRequested toolCallRequested => new EventResponseItem(
                    EventType: nameof(ToolCallRequested),
                    AgentId: toolCallRequested.Agent.Id,
                    Details: new
                    {
                        toolCall = toolCallRequested.toolCall
                    }),
                ToolCallCompleted toolCallCompleted => new EventResponseItem(
                    EventType: nameof(ToolCallCompleted),
                    AgentId: toolCallCompleted.Agent.Id,
                    Details: new
                    {
                        toolCall = toolCallCompleted.toolCall,
                        resultText = toolCallCompleted.resultText
                    }),
                TurnCompleted turnCompleted => new EventResponseItem(
                    EventType: nameof(TurnCompleted),
                    AgentId: turnCompleted.Agent.Id,
                    Details: new
                    {
                        agent = turnCompleted.Agent
                    }),
                _ => new EventResponseItem(
                    EventType: evt.GetType().Name,
                    AgentId: evt.Agent.Id,
                    Details: evt)
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

// Send a message within an agent
app.MapPost("/api/agents/{agentId}/messages", async (Guid agentId, SendMessageRequest body) =>
{
    try
    {
        string response = await Agent.HandleMessageAsync(agentId, body.message);
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

record CreateAgentRequest(
    string? Model,
    string? Instructions,
    string? PromptCacheKey,
    ServiceTier? Tier,
    ReasoningEffort? Reasoning,
    TextVerbosity? Verbosity,
    string? Toolkit);

record SendMessageRequest(string message);

record EventResponseItem(string EventType, Guid AgentId, object Details);
