using Agent.Llm;
using Agent.Tools;
using System.Text.Json;

namespace Tests;

public class EnumSerialisationTests
{
    [Fact]
    public void SessionEnumsSerialiseAsText()
    {
        var session = new Session(
            model: "gpt-5-nano",
            instructions: "test",
            promptCacheKey: "test",
            tier: ServiceTier.Priority,
            reasoning: ReasoningEffort.XHigh,
            toolkit: new Toolkit("test"));

        string json = JsonSerializer.Serialize(session);

        Assert.Contains("\"Tier\":\"Priority\"", json);
        Assert.Contains("\"Reasoning\":\"XHigh\"", json);
    }

    [Fact]
    public void ToolParameterEnumSerialisesAsText()
    {
        var parameter = new ToolParameter
        {
            Name = "count",
            Kind = ToolValueKind.Integer
        };

        string json = JsonSerializer.Serialize(parameter);

        Assert.Contains("\"Kind\":\"Integer\"", json);
    }
}
