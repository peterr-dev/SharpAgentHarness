using System.Text.Json;
using Core;

namespace Tools
{

    /// <summary>
    /// Example tool that returns the current time in ISO 8601 format.
    /// </summary>
    public sealed class GetCurrentTimeTool : Tool
    {
        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public GetCurrentTimeTool()
        {
            Name = "get_current_time";
            Description = "Get the current time in ISO 8601 format for a specified timezone.";

            Parameters =
            [
                new ToolParameter
                {
                    Name = "timezone",
                    Kind = ToolValueKind.String,
                    Description = "IANA or system timezone ID (for example, 'UTC' or 'America/New_York').",
                    Nullable = true
                }
            ];
        }

        public override Task<string> ExecuteAsync(string argumentsJson)
        {
            string timezone = ExtractTimezone(argumentsJson) ?? "UTC";

            TimeZoneInfo zone = ResolveTimeZone(timezone);
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            DateTimeOffset localTime = TimeZoneInfo.ConvertTime(nowUtc, zone);

            string isoTime = localTime.ToString("O");
            return Task.FromResult(isoTime);
        }

        private static string? ExtractTimezone(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(argumentsJson);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("timezone", out JsonElement timezoneElement))
            {
                return null;
            }

            if (timezoneElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? timezone = timezoneElement.GetString();
            return string.IsNullOrWhiteSpace(timezone) ? null : timezone;
        }

        private static TimeZoneInfo ResolveTimeZone(string timezone)
        {
            // Fall back to UTC for unknown timezone IDs to keep the tool simple.
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timezone);
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.Utc;
            }
            catch (InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
        }
    }
}
