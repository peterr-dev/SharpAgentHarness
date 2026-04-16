using Core.Llm;

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

    public abstract class LlmRequestReadyHook : Hook
    {
        public virtual void OnLlmRequestReady(Session session, Request req)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (req == null) throw new ArgumentNullException(nameof(req));
        }
    }

    public abstract class RawLlmRequestReadyHook : Hook
    {
        public virtual void OnRawLlmRequestReady(Session session, HttpRequestMessage req, string requestBody)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (requestBody == null) throw new ArgumentNullException(nameof(requestBody));
        }
    }

    public abstract class LlmResponseReceivedHook : Hook
    {
        public virtual void OnLlmResponseReceived(Session session, Response resp)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (resp == null) throw new ArgumentNullException(nameof(resp));
        }
    }

    public abstract class ToolCallRequestedHook : Hook
    {
        public virtual void OnToolCallRequested(Session session, ResponseOutputItemFunctionCall toolCall)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (toolCall == null || string.IsNullOrEmpty(toolCall.Id) || string.IsNullOrEmpty(toolCall.Name)) throw new ArgumentNullException(nameof(toolCall));
         }
    }

    public abstract class ToolCallCompletedHook : Hook
    {
        public virtual void OnToolCallCompleted(Session session, ResponseOutputItemFunctionCall toolCall, string resultText)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (toolCall == null) throw new ArgumentNullException(nameof(toolCall));
            if (resultText == null) throw new ArgumentNullException(nameof(resultText));
        }
    }

    public abstract class TurnCompletedHook : Hook
    {
        public virtual void OnTurnCompleted(Session session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
        }
    }

    public class LogEventTurnStartedHook : TurnStartedHook
    {
        public override void OnTurnStarted(Session session)
        {
            base.OnTurnStarted(session);
            EventTraces.Publish(new TurnStarted(session));
        }
    }

    public class LogEventLlmRequestReadyHook : LlmRequestReadyHook
    {
        public override void OnLlmRequestReady(Session session, Request req)
        {
            base.OnLlmRequestReady(session, req);
            EventTraces.Publish(new LlmRequestReady(session, req));
        }
    }

    public class LogEventRawLlmRequestReadyHook : RawLlmRequestReadyHook
    {
        public override void OnRawLlmRequestReady(Session session, HttpRequestMessage req, string requestBody)
        {
            base.OnRawLlmRequestReady(session, req, requestBody);
            EventTraces.Publish(new RawLlmRequestReady(session, req, requestBody));
        }
    }

    public class LogEventLlmResponseReceivedHook : LlmResponseReceivedHook
    {
        public override void OnLlmResponseReceived(Session session, Response resp)
        {
            base.OnLlmResponseReceived(session, resp);
            EventTraces.Publish(new LlmResponseReceived(session, resp));
        }
    }

    public class LogEventToolCallRequestedHook : ToolCallRequestedHook
    {
        public override void OnToolCallRequested(Session session, ResponseOutputItemFunctionCall toolCall)
        {
            base.OnToolCallRequested(session, toolCall);
            EventTraces.Publish(new ToolCallRequested(session, toolCall));
        }
    }

    public class LogEventToolCallCompletedHook : ToolCallCompletedHook
    {
        public override void OnToolCallCompleted(Session session, ResponseOutputItemFunctionCall toolCall, string resultText)
        {
            base.OnToolCallCompleted(session, toolCall, resultText);
            EventTraces.Publish(new ToolCallCompleted(session, toolCall, resultText));
        }
    }

    public class LogEventTurnCompletedHook : TurnCompletedHook
    {
        public override void OnTurnCompleted(Session session)
        {
            base.OnTurnCompleted(session);
            EventTraces.Publish(new TurnCompleted(session));
        }
    }

    // Centralised registry for Hook instances. Register hooks once at startup,
    // then retrieve them by base type wherever they need to be invoked.
    public static class HookRegistry
    {
        private static readonly List<Hook> _hooks = new();

        // Registers one instance of each LogEvent* hook so that all lifecycle
        // events for a Session are always recorded.
        static HookRegistry()
        {
            _hooks.Add(new LogEventTurnStartedHook());
            _hooks.Add(new LogEventLlmRequestReadyHook());
            _hooks.Add(new LogEventRawLlmRequestReadyHook());
            _hooks.Add(new LogEventLlmResponseReceivedHook());
            _hooks.Add(new LogEventToolCallRequestedHook());
            _hooks.Add(new LogEventToolCallCompletedHook());
            _hooks.Add(new LogEventTurnCompletedHook());
        }

        public static void Register(Hook hook)
        {
            if (hook == null) throw new ArgumentNullException(nameof(hook));
            _hooks.Add(hook);
        }

        // Returns all registered hooks that are assignable to type T.
        public static IEnumerable<T> GetAll<T>() where T : Hook
        {
            return _hooks.OfType<T>();
        }

        public static void RunTurnStartedHooks(Session session)
            => GetAll<TurnStartedHook>().ToList().ForEach(h => h.OnTurnStarted(session));

        public static void RunLlmRequestReadyHooks(Session session, Request req)
            => GetAll<LlmRequestReadyHook>().ToList().ForEach(h => h.OnLlmRequestReady(session, req));

        public static void RunLlmRawRequestReadyHooks(Session session, HttpRequestMessage req, string requestBody)
            => GetAll<RawLlmRequestReadyHook>().ToList().ForEach(h => h.OnRawLlmRequestReady(session, req, requestBody));

        public static void RunLlmResponseReceivedHooks(Session session, Response resp)
            => GetAll<LlmResponseReceivedHook>().ToList().ForEach(h => h.OnLlmResponseReceived(session, resp));

        public static void RunToolCallRequestedHooks(Session session, ResponseOutputItemFunctionCall toolCall)
            => GetAll<ToolCallRequestedHook>().ToList().ForEach(h => h.OnToolCallRequested(session, toolCall));

        public static void RunToolCallCompletedHooks(Session session, ResponseOutputItemFunctionCall toolCall, string resultText)
            => GetAll<ToolCallCompletedHook>().ToList().ForEach(h => h.OnToolCallCompleted(session, toolCall, resultText));

        public static void RunTurnCompletedHooks(Session session)
            => GetAll<TurnCompletedHook>().ToList().ForEach(h => h.OnTurnCompleted(session));
    }
}
