using Agent.Tools;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace Agent.Llm
{
    // Serialise enum values as readable strings in API responses.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ServiceTier
    {
        Auto,
        Default,
        Flex,
        Priority
    }

    // Serialise enum values as readable strings in API responses.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ReasoningEffort
    {
        None,
        Minimal,
        Low,
        Medium,
        High,
        XHigh
    }

    // Serialise enum values as readable strings in API responses.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TextVerbosity
    {
        Low,
        Medium,
        High
    }

    public static class Sessions
    {
        private static readonly ConcurrentDictionary<Guid, Session> _sessions = new();

        public static void Add(Session session)
        {
            _sessions[session.Id] = session;
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

        public static void Clear()
        {
            _sessions.Clear();
        }
    }

    public class Session
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Model { get; init; }

        public ServiceTier Tier { get; init; }

        public string PromptCacheKey { get; init; }

        public ReasoningEffort Reasoning { get; init; }

        public TextVerbosity Verbosity { get; init; }

        /// <summary>
        /// Populated from turn two onwards
        /// </summary>
        public string? PreviousResponseId { get; set; }

        /// <summary>
        /// Equivalent to a System Prompt
        /// </summary>
        public string Instructions { get; init; }

        public string ToolkitName { get; init; }

        /// <summary>
        /// Running token usage totals accumulated across all successful LLM responses in this session.
        /// </summary>
        public SessionUsageTotals UsageTotals { get; } = new();

        [JsonIgnore]
        public Toolkit Toolkit { get; }

        private readonly Turn _turn;

        public Session(string model, string instructions, string promptCacheKey, ServiceTier tier, ReasoningEffort reasoning, TextVerbosity verbosity, Toolkit toolkit, int maxIterations = 5)
        {
            if (toolkit is null) throw new ArgumentNullException(nameof(toolkit));

            Model = model;
            Instructions = instructions;
            PromptCacheKey = promptCacheKey;
            Tier = tier;
            Reasoning = reasoning;
            Verbosity = verbosity;
            ToolkitName = toolkit.Name;
            Toolkit = toolkit;
            _turn = new Turn(maxIterations);
        }

        public async Task<string> SendMessage(string userMessage)
        {
            string result = await _turn.RunTurnAsync(this, userMessage);
            return result;
        }
    }

    public class SessionUsageTotals
    {
        /// <summary>
        /// Total input tokens across all responses in the session.
        /// </summary>
        public int InputTokens { get; private set; }

        /// <summary>
        /// Total cached input tokens across all responses in the session.
        /// </summary>
        public int CachedInputTokens { get; private set; }

        /// <summary>
        /// Total output tokens across all responses in the session.
        /// </summary>
        public int OutputTokens { get; private set; }

        /// <summary>
        /// Total reasoning output tokens across all responses in the session.
        /// </summary>
        public int ReasoningOutputTokens { get; private set; }

        /// <summary>
        /// Adds a response usage snapshot into the running totals.
        /// </summary>
        public void Add(ResponseUsage? usage)
        {
            if (usage is null)
            {
                return;
            }

            InputTokens += usage.InputTokens ?? 0;
            CachedInputTokens += usage.CachedInputTokens ?? 0;
            OutputTokens += usage.OutputTokens ?? 0;
            ReasoningOutputTokens += usage.ReasoningOutputTokens ?? 0;
        }
    }
}
