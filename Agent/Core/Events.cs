using Core.ChatCompletions;

namespace Core
{
    public abstract class Event
    {
        public Session Session { get; }

        protected Event(Session session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }
    }

    public sealed class TurnStarted : Event
    {
        public TurnStarted(Session session) : base(session) { }
    }

    public sealed class RequestReady : Event
    {
        public Request Request { get; }

        public RequestReady(Session session, Request request) : base(session)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
        }
    }

    public sealed class ResponseReceived : Event
    {
        public Response Response { get; }

        public ResponseReceived(Session session, Response response) : base(session)
        {
            Response = response ?? throw new ArgumentNullException(nameof(response));
        }
    }

    public sealed class RawRequestReady : Event
    {
        public string RawRequest { get; }

        public RawRequestReady(Session session, string rawRequest) : base(session)
        {
            RawRequest = rawRequest ?? throw new ArgumentNullException(nameof(rawRequest));
        }
    }

    public sealed class RawResponseReceived : Event
    {
        public string RawResponse { get; }

        public RawResponseReceived(Session session, string rawResponse) : base(session)
        {
            RawResponse = rawResponse ?? throw new ArgumentNullException(nameof(rawResponse));
        }
    }

    public sealed class TurnCompleted : Event
    {
        public TurnCompleted(Session session) : base(session) { }
    }

    public static class Events
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

        public static IReadOnlyList<Event> GetEventsForSession(Session session)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));

            return GetEventsForSession(session.Id);
        }

        public static IReadOnlyList<Event> GetEventsForSession(Guid sessionId)
        {
            lock (_lock)
            {
                return _events
                    .OfType<Event>()
                    .Where(evt => evt.Session.Id == sessionId)
                    .ToList();
            }
        }

        public static IReadOnlyList<TEvent> GetEventsForSession<TEvent>(Session session) where TEvent : Event
        {
            if (session is null) throw new ArgumentNullException(nameof(session));

            return GetEventsForSession<TEvent>(session.Id);
        }

        public static IReadOnlyList<TEvent> GetEventsForSession<TEvent>(Guid sessionId) where TEvent : Event
        {
            lock (_lock)
            {
                return _events
                    .OfType<TEvent>()
                    .Where(evt => evt.Session.Id == sessionId)
                    .ToList();
            }
        }
    }
}
