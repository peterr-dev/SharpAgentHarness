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

    public static class Agents
    {
        private static readonly ConcurrentDictionary<Guid, Agent> _agents = new();

        public static void Add(Agent agent)
        {
            _agents[agent.Id] = agent;
        }

        public static Agent GetAgent(Guid agentId)
        {
            if (_agents.TryGetValue(agentId, out var agent))
                return agent;

            throw new KeyNotFoundException($"Agent with ID '{agentId}' not found.");
        }

        public static bool Remove(Guid agentId)
        {
            return _agents.TryRemove(agentId, out _);
        }

        public static void Clear()
        {
            _agents.Clear();
        }
    }

    public class Agent
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
        /// Running token usage totals accumulated across all successful LLM responses in this agent.
        /// </summary>
        public AgentUsageTotals UsageTotals { get; } = new();

        [JsonIgnore]
        public Toolkit Toolkit { get; }

        private readonly Turn _turn;

        public Agent(string model, string instructions, string promptCacheKey, ServiceTier tier, ReasoningEffort reasoning, TextVerbosity verbosity, Toolkit toolkit, int maxIterations = 5)
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

        /// <summary>
        /// Send an incoming user message for an agent turn.
        /// </summary>
        public static async Task<string> HandleMessageAsync(Guid agentId, string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                throw new ArgumentException("User message is required.", nameof(userMessage));

            var agent = Agents.GetAgent(agentId);
            string response = await agent.SendMessage(userMessage);
            return response;
        }
    }

    public class AgentUsageTotals
    {
        /// <summary>
        /// Total input tokens across all responses in the agent.
        /// </summary>
        public int InputTokens { get; private set; }

        /// <summary>
        /// Total cached input tokens across all responses in the agent.
        /// </summary>
        public int CachedInputTokens { get; private set; }

        /// <summary>
        /// Total output tokens across all responses in the agent.
        /// </summary>
        public int OutputTokens { get; private set; }

        /// <summary>
        /// Total reasoning output tokens across all responses in the agent.
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
