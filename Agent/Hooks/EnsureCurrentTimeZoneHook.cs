using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Core;
using Core.Llm;

namespace Hooks
{
    /// <summary>
    /// Example hook that ensures the timezone defaults to UK when using GetCurrentTimeTool.
    /// </summary>
    public class EnsureCurrentTimeZoneHook : ToolCallRequestedHook
    {
        public sealed record EnsureCurrentTimeZoneHookFired([property: JsonIgnore] Session session, ResponseOutputItemFunctionCall toolCall) : ISessionEvent
        {
            public Session Session => session;
        }

        public override void OnToolCallRequested(Session session, ResponseOutputItemFunctionCall toolCall)
        {
            base.OnToolCallRequested(session, toolCall);

            // Only apply this hook for the get_current_time tool.
            if (toolCall.Name!.Equals("get_current_time", StringComparison.OrdinalIgnoreCase))
            {
                // Default "timezone" tool call parameter to "Europe/London" if not already set.
                JsonObject arguments = ParseArguments(toolCall.Arguments);

                if (!TryGetTimezone(arguments, out string? timezone) || string.IsNullOrWhiteSpace(timezone))
                {
                    arguments["timezone"] = "Europe/London";
                    toolCall.Arguments = arguments.ToJsonString();
                    EventTraces.Publish(new EnsureCurrentTimeZoneHookFired(session, toolCall));
                }
            }
        }

        private static JsonObject ParseArguments(string? argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return new JsonObject();
            }

            JsonNode? parsedNode = JsonNode.Parse(argumentsJson);
            return parsedNode as JsonObject ?? new JsonObject();
        }

        private static bool TryGetTimezone(JsonObject arguments, out string? timezone)
        {
            timezone = null;

            if (!arguments.TryGetPropertyValue("timezone", out JsonNode? timezoneNode))
            {
                return false;
            }

            if (timezoneNode is not JsonValue timezoneValue)
            {
                return false;
            }

            if (!timezoneValue.TryGetValue(out timezone))
            {
                return false;
            }

            return true;
        }
    }
}