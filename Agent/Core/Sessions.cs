using System.Collections.Concurrent;
using Core.ChatCompletions;

namespace Core
{
    public class Session
    {
        public Guid Id { get; } = Guid.NewGuid();

        public List<ChatCompletionMessageParam> Messages { get; } = new();

        public Uri ChatCompletionsUri { get; set; }

        public Session(string instructions, Uri chatCompletionsUri)
        {
            Messages.Add(new ChatCompletionDeveloperMessageParam
            {
                Content = instructions
            });
            ChatCompletionsUri = chatCompletionsUri;
        }

        public int TotalInputTokens { get; private set; }

        public int TotalCachedInputTokens { get; private set; }

        public int TotalOutputTokens { get; private set; }

        public int TotalReasoningOutputTokens { get; private set; }

        public void AddUsage(ChatCompletionUsage usage)
        {
            TotalInputTokens += usage.InputTokens;
            TotalCachedInputTokens += usage.CachedInputTokens;
            TotalOutputTokens += usage.OutputTokens;
            TotalReasoningOutputTokens += usage.ReasoningOutputTokens;
        }
    }

    public static class Sessions
    {
        private static readonly ConcurrentDictionary<Guid, Session> _sessions = new();

        public static Session CreateSession(string instructions, Uri chatCompletionsUri)
        {
            Session session = new Session(instructions, chatCompletionsUri);
            _sessions[session.Id] = session;
            return session;
        }

        public static Session GetSession(Guid sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
                return session;

            throw new KeyNotFoundException($"Session with ID '{sessionId}' not found.");
        }

        public static bool Remove(Guid sessionId)
        {
            return _sessions.TryRemove(sessionId, out _);
        }
    }
}
