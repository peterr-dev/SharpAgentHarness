using Agent.Llm;

namespace Agent
{
    public interface ISessionEvent
    {
        Session Session { get; }
    }

    public sealed record TurnStarted(Session session) : ISessionEvent
    {
        public Session Session => session;
    }

    public sealed record LlmRequestSent(Session session, Request req) : ISessionEvent
    {
        public Session Session => session;
    }

    public sealed record LlmResponseReceived(Session session, Response resp) : ISessionEvent
    {
        public Session Session => session;
    }

    public sealed record ToolCallRequested(Session session, ResponseOutputItemFunctionCall toolCall) : ISessionEvent
    {
        public Session Session => session;
    }

    public sealed record ToolCallCompleted(Session session, ResponseOutputItemFunctionCall toolCall, string resultText) : ISessionEvent
    {
        public Session Session => session;
    }

    public sealed record TurnCompleted(Session session) : ISessionEvent
    {
        public Session Session => session;
    }

    public sealed record LlmRawRequestSent(Session session, string RequestBody) : ISessionEvent
    {
        public Session Session => session;
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

        public static IReadOnlyList<ISessionEvent> GetEventsForSession(Session session)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));

            return GetEventsForSession(session.Id);
        }

        public static IReadOnlyList<ISessionEvent> GetEventsForSession(Guid sessionId)
        {
            lock (_lock)
            {
                return _events
                    .OfType<ISessionEvent>()
                    .Where(evt => evt.Session.Id == sessionId)
                    .ToList();
            }
        }

        public static IReadOnlyList<TEvent> GetEventsForSession<TEvent>(Session session) where TEvent : ISessionEvent
        {
            if (session is null) throw new ArgumentNullException(nameof(session));

            return GetEventsForSession<TEvent>(session.Id);
        }

        public static IReadOnlyList<TEvent> GetEventsForSession<TEvent>(Guid sessionId) where TEvent : ISessionEvent
        {
            lock (_lock)
            {
                return _events
                    .OfType<TEvent>()
                    .Where(evt => evt.Session.Id == sessionId)
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
