using System.Text.Json.Serialization;

namespace Core
{
    // Serialise enum values as readable strings in API responses.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ToolValueKind
    {
        String,
        Integer,
        Number,
        Boolean,
        Object,
        Array
    }

    public class ToolParameter
    {
        public required string Name { get; set; }
        public required ToolValueKind Kind { get; set; }
        public string? Description { get; set; }
        public bool Nullable { get; set; }
        public List<string>? EnumValues { get; set; }
        public List<ToolParameter>? Properties { get; set; }
        public ToolParameter? Items { get; set; }
    }

    public abstract class Tool
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
        public List<ToolParameter> Parameters { get; set; } = new List<ToolParameter>();
        public abstract Task<string> ExecuteAsync(string argumentsJson);
        
    }

    public class Toolkit
    {
        // Use a concurrent dictionary keyed by lower-case tool name for thread-safe registration and lookup.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Tool> _tools =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Tool>(StringComparer.OrdinalIgnoreCase);

        public string Name { get; }

        public Toolkit(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Toolkit name is required.", nameof(name));

            Name = name;
        }

        // Read-only snapshot of registered tools, safe to enumerate at any time.
        public IReadOnlyCollection<Tool> Tools => _tools.Values.ToArray();

        public void Add(Tool tool)
        {
            if (tool is null) throw new ArgumentNullException(nameof(tool));

            // TryAdd returns false if a key with the same name already exists.
            if (!_tools.TryAdd(tool.Name, tool))
                throw new InvalidOperationException($"A tool with the name '{tool.Name}' is already registered.");
        }

        public Task<string> ExecuteAsync(string toolName, string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                throw new ArgumentException("Tool name is required.", nameof(toolName));

            if (!_tools.TryGetValue(toolName, out var tool))
                throw new InvalidOperationException($"Tool '{toolName}' is not registered.");

            return tool.ExecuteAsync(argumentsJson ?? "{}");
        }
    }

    public static class Toolkits
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Toolkit> _toolkits =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Toolkit>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyCollection<Toolkit> All => _toolkits.Values.ToArray();

        public static void Add(Toolkit toolkit)
        {
            if (toolkit is null) throw new ArgumentNullException(nameof(toolkit));

            if (!_toolkits.TryAdd(toolkit.Name, toolkit))
                throw new InvalidOperationException($"A toolkit with the name '{toolkit.Name}' is already registered.");
        }

        public static Toolkit Get(string toolkitName)
        {
            if (string.IsNullOrWhiteSpace(toolkitName))
                throw new ArgumentException("Toolkit name is required.", nameof(toolkitName));

            if (_toolkits.TryGetValue(toolkitName, out var toolkit))
                return toolkit;

            throw new KeyNotFoundException($"Toolkit '{toolkitName}' is not registered.");
        }

        public static void Clear()
        {
            _toolkits.Clear();
        }
    }
}
