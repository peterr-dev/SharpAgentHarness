using System.Collections.Concurrent;
using Core.ChatCompletions;

namespace Core
{
    public class Session
    {
        private const int DefaultMaxTurnIterations = 5;
        private readonly List<ChatCompletionMessageParam> _messages = new();
        private readonly object _messagesLock = new();
        private readonly object _usageLock = new();
        private readonly SemaphoreSlim _turnSemaphore = new(1, 1);

        public Session() : this(new ApiClient())
        {
        }

        public Session(ApiClient apiClient)
        {
            ApiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _turn = new Turn(ApiClient, DefaultMaxTurnIterations);
        }

        public Guid Id { get; } = Guid.NewGuid();

        public Uri ChatCompletionsUrl { get; init; } = new Uri("https://api.openai.com/v1/chat/completions");

        public required string Model { get; init; }

        public required string PromptCacheKey { get; init; }

        public ApiClient ApiClient { get; }

        private readonly Turn _turn;

        public Task<ChatCompletionMessage> RunTurnAsync(ChatCompletionMessageParam message, CancellationToken cancellationToken)
        {
            return _turn.RunTurnAsync(this, message, cancellationToken);
        }

        public Request CreateRequest(IEnumerable<ChatCompletionMessageParam> messages)
        {
            if (messages is null) throw new ArgumentNullException(nameof(messages));

            Request request = new Request(this);
            request.Messages.AddRange(messages);
            return request;
        }

        // Return a copy so callers can safely enumerate without observing concurrent mutations.
        public List<ChatCompletionMessageParam> Messages
        {
            get
            {
                lock (_messagesLock)
                {
                    return new List<ChatCompletionMessageParam>(_messages);
                }
            }
        }

        public void AddMessage(ChatCompletionMessageParam message)
        {
            lock (_messagesLock)
            {
                _messages.Add(message);
            }
        }

        // Usage totals are cumulative across all model calls in the session.
        public int TotalInputTokens { get; private set; }

        public int TotalCachedInputTokens { get; private set; }

        public int TotalOutputTokens { get; private set; }

        public int TotalReasoningOutputTokens { get; private set; }

        public void AddUsage(ChatCompletionUsage usage)
        {
            if (usage is null) throw new ArgumentNullException(nameof(usage));

            lock (_usageLock)
            {
                TotalInputTokens += usage.InputTokens;
                TotalCachedInputTokens += usage.CachedInputTokens;
                TotalOutputTokens += usage.OutputTokens;
                TotalReasoningOutputTokens += usage.ReasoningOutputTokens;
            }
        }

        public Task WaitForTurnAsync(CancellationToken cancellationToken)
        {
            return _turnSemaphore.WaitAsync(cancellationToken);
        }

        public void ReleaseTurn()
        {
            _turnSemaphore.Release();
        }

        public required ReasoningEffort ReasoningEffort { get; init; }

        public required Verbosity Verbosity { get; init; }

        public required ServiceTier ServiceTier { get; init; }

        public Toolkit? Toolkit { get; init;}

        public double? Temperature { get; init; }

        public int? MaxCompletionTokens { get; init; }
    }

    public static class Sessions
    {
        private static readonly ConcurrentDictionary<Guid, Session> _sessions = new();

        public static Session CreateSession(Session session)
        {
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
