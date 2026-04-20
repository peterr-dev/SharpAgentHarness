using Core.ChatCompletions;

namespace Core
{    public class Toolkit
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ChatCompletionTool> _tools = new(StringComparer.OrdinalIgnoreCase);

        public string Name { get; }

        public Toolkit(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Toolkit name is required.", nameof(name));

            Name = name;
        }

        public List<ChatCompletionTool> Tools => _tools.Values.ToList();

        public void Add(ChatCompletionTool tool)
        {
            if (tool is null) throw new ArgumentNullException(nameof(tool));

            if (!_tools.TryAdd(tool.Name, tool))
                throw new InvalidOperationException($"A tool with the name '{tool.Name}' is already registered.");
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
    }
}