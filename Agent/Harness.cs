using Agent.Llm;
using Agent.Tools;

namespace Agent
{
    /// <summary>
    /// The Harness is the entry point for the agent.
    /// </summary>
    public class Harness
    {
        /// <summary>
        /// Send an incoming user message for an agent turn.
        /// </summary>
        public async Task<string> HandleMessageAsync(Guid sessionId, string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                throw new ArgumentException("User message is required.", nameof(userMessage));

            var session = Sessions.GetSession(sessionId);
            string response = await session.SendMessage(userMessage);
            return response;
        }
    }
}
