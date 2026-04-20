using System.Text.Json;
using Core.ChatCompletions;

public sealed class GetCurrentTimeTool : ChatCompletionFunctionTool
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public GetCurrentTimeTool() : base()
    {
        Name = "get_current_time";
        Description = "Get the current time in ISO 8601 format for a specified timezone.";
        Strict = true;
        Parameters.Add(new FunctionToolParameter
        {
            Name = "timezone",
            Description = "The IANA timezone identifier (e.g., 'America/New_York'). If not provided, defaults to UTC.",
            Type = FunctionToolCallParameterType.String
        });
    }

    public override async Task<string> ExecuteAsync(string argumentsJson)
    {
        string timezone = ExtractTimezone(argumentsJson) ?? "UTC";

        TimeZoneInfo zone = ResolveTimeZone(timezone);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        DateTimeOffset localTime = TimeZoneInfo.ConvertTime(nowUtc, zone);

        string isoTime = localTime.ToString("O");
        return isoTime;
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
