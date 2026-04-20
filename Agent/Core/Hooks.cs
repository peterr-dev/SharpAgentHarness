using Core.ChatCompletions;

namespace Core
{
    public abstract class Hook { }

    public abstract class TurnStartedHook : Hook
    {
        public virtual void OnTurnStarted(Session session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
        }
    }

    public abstract class RequestReadyHook : Hook
    {
        public virtual void OnRequestReady(Session session, Request request)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (request == null) throw new ArgumentNullException(nameof(request));
        }
    }

    public abstract class ResponseReceivedHook : Hook
    {
        public virtual void OnResponseReceived(Session session, Response response)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (response == null) throw new ArgumentNullException(nameof(response));
        }
    }

    public abstract class TurnCompletedHook : Hook
    {
        public virtual void OnTurnCompleted(Session session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
        }
    }

    public abstract class RawRequestReadyHook : Hook
    {
        public virtual void OnRawRequestReady(Session session, string rawRequest)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (rawRequest == null) throw new ArgumentNullException(nameof(rawRequest));
        }
    }

    public abstract class RawResponseReceivedHook : Hook
    {
        public virtual void OnRawResponseReceived(Session session, string rawResponse)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (rawResponse == null) throw new ArgumentNullException(nameof(rawResponse));
        }
    }

    public class LogEventTurnStartedHook : TurnStartedHook
    {
        public override void OnTurnStarted(Session session)
        {
            base.OnTurnStarted(session);
            Events.Publish(new TurnStarted(session));
        }
    }

    public class LogEventRequestReadyHook : RequestReadyHook
    {
        public override void OnRequestReady(Session session, Request request)
        {
            base.OnRequestReady(session, request);
            Events.Publish(new RequestReady(session, request));
        }
    }

    public class LogEventResponseReceivedHook : ResponseReceivedHook
    {
        public override void OnResponseReceived(Session session, Response response)
        {
            base.OnResponseReceived(session, response);
            Events.Publish(new ResponseReceived(session, response));
        }
    }

    public class LogEventTurnCompletedHook : TurnCompletedHook
    {
        public override void OnTurnCompleted(Session session)
        {
            base.OnTurnCompleted(session);
            Events.Publish(new TurnCompleted(session));
        }
    }

    public class LogEventRawRequestReadyHook : RawRequestReadyHook
    {
        public override void OnRawRequestReady(Session session, string rawRequest)
        {
            base.OnRawRequestReady(session, rawRequest);
            Events.Publish(new RawRequestReady(session, rawRequest));
        }
    }

    public class LogEventRawResponseReceivedHook : RawResponseReceivedHook
    {
        public override void OnRawResponseReceived(Session session, string rawResponse)
        {
            base.OnRawResponseReceived(session, rawResponse);
            Events.Publish(new RawResponseReceived(session, rawResponse));
        }
    }

    // Centralised registry for Hook instances. Register hooks once at startup,
    // then retrieve them by base type wherever they need to be invoked.
    public static class HookRegistry
    {
        private static readonly List<Hook> _hooks = new();
        private static readonly object _hooksLock = new();

        // Registers one instance of each LogEvent* hook so that all lifecycle
        // events for a Session are always recorded.
        static HookRegistry()
        {
            _hooks.Add(new LogEventTurnStartedHook());
            _hooks.Add(new LogEventRequestReadyHook());
            _hooks.Add(new LogEventResponseReceivedHook());
            _hooks.Add(new LogEventTurnCompletedHook());
            _hooks.Add(new LogEventRawRequestReadyHook());
            _hooks.Add(new LogEventRawResponseReceivedHook());
        }

        public static void Register(Hook hook)
        {
            if (hook == null) throw new ArgumentNullException(nameof(hook));
            lock (_hooksLock)
            {
                _hooks.Add(hook);
            }
        }

        // Returns a snapshot of hooks assignable to T so callers can safely iterate
        // while other threads register additional hooks.
        public static IReadOnlyList<T> GetAll<T>() where T : Hook
        {
            lock (_hooksLock)
            {
                return _hooks.OfType<T>().ToList();
            }
        }

        public static void RunTurnStartedHooks(Session session)
            => GetAll<TurnStartedHook>().ToList().ForEach(h => h.OnTurnStarted(session));

        public static void RunRequestReadyHooks(Session session, Request request)
            => GetAll<RequestReadyHook>().ToList().ForEach(h => h.OnRequestReady(session, request));

        public static void RunResponseReceivedHooks(Session session, Response response)
            => GetAll<ResponseReceivedHook>().ToList().ForEach(h => h.OnResponseReceived(session, response));

        public static void RunTurnCompletedHooks(Session session)
            => GetAll<TurnCompletedHook>().ToList().ForEach(h => h.OnTurnCompleted(session));

        public static void RunRawRequestReadyHooks(Session session, string rawRequest)
            => GetAll<RawRequestReadyHook>().ToList().ForEach(h => h.OnRawRequestReady(session, rawRequest));

        public static void RunRawResponseReceivedHooks(Session session, string rawResponse)
            => GetAll<RawResponseReceivedHook>().ToList().ForEach(h => h.OnRawResponseReceived(session, rawResponse));
    }
}
