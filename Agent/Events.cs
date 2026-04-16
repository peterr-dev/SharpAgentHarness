using Agent.Llm;

namespace Agent
{
    public interface IAgentEvent
    {
        Agent Agent { get; }
    }

    public sealed record TurnStarted(Agent agent) : IAgentEvent
    {
        public Agent Agent => agent;
    }

    public sealed record LlmRequestSent(Agent agent, Request req) : IAgentEvent
    {
        public Agent Agent => agent;
    }

    public sealed record LlmResponseReceived(Agent agent, Response resp) : IAgentEvent
    {
        public Agent Agent => agent;
    }

    public sealed record ToolCallRequested(Agent agent, ResponseOutputItemFunctionCall toolCall) : IAgentEvent
    {
        public Agent Agent => agent;
    }

    public sealed record ToolCallCompleted(Agent agent, ResponseOutputItemFunctionCall toolCall, string resultText) : IAgentEvent
    {
        public Agent Agent => agent;
    }

    public sealed record TurnCompleted(Agent agent) : IAgentEvent
    {
        public Agent Agent => agent;
    }

    public sealed record LlmRawRequestSent(Agent agent, string RequestBody) : IAgentEvent
    {
        public Agent Agent => agent;
    }

    public static class EventTraces
    {
        private static readonly List<object> _events = new();
        private static readonly object _lock = new();

        public static void Publish<TEvent>(TEvent evt)
        {
            if (evt is null) throw new ArgumentNullException(nameof(evt));

            lock (_lock)
            {
                _events.Add(evt!);
            }
        }

        public static IReadOnlyList<object> GetEvents()
        {
            lock (_lock)
            {
                return _events.ToList();
            }
        }

        public static IReadOnlyList<TEvent> GetEvents<TEvent>()
        {
            lock (_lock)
            {
                return _events.OfType<TEvent>().ToList();
            }
        }

        public static IReadOnlyList<IAgentEvent> GetEventsForAgent(Agent agent)
        {
            if (agent is null) throw new ArgumentNullException(nameof(agent));

            return GetEventsForAgent(agent.Id);
        }

        public static IReadOnlyList<IAgentEvent> GetEventsForAgent(Guid agentId)
        {
            lock (_lock)
            {
                return _events
                    .OfType<IAgentEvent>()
                    .Where(evt => evt.Agent.Id == agentId)
                    .ToList();
            }
        }

        public static IReadOnlyList<TEvent> GetEventsForAgent<TEvent>(Agent agent) where TEvent : IAgentEvent
        {
            if (agent is null) throw new ArgumentNullException(nameof(agent));

            return GetEventsForAgent<TEvent>(agent.Id);
        }

        public static IReadOnlyList<TEvent> GetEventsForAgent<TEvent>(Guid agentId) where TEvent : IAgentEvent
        {
            lock (_lock)
            {
                return _events
                    .OfType<TEvent>()
                    .Where(evt => evt.Agent.Id == agentId)
                    .ToList();
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _events.Clear();
            }
        }
    }
}
