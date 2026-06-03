using Aion.Core.Interfaces;
using Aion.Core.Models;
using System.Collections.Concurrent;

namespace Aion.Core.Tools;

public class ToolRegistry
{
    private readonly ConcurrentDictionary<string, ITool> _tools = new();
    private readonly ConcurrentDictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public bool Unregister(string name)
    {
        return _tools.TryRemove(name, out _);
    }

    public void RegisterAlias(string alias, string canonicalName)
    {
        _aliases[alias] = canonicalName;
    }

    public bool RemoveAlias(string alias)
    {
        return _aliases.TryRemove(alias, out _);
    }

    /// <summary>
    /// Register a dynamically created tool (agent-defined code).
    /// The tool invokes the sandbox executor when called.
    /// </summary>
    public void RegisterDynamic(string name, string description, string code, string language, ISandboxExecutor sandbox)
    {
        var tool = new DynamicTool(name, description, code, language, sandbox);
        Register(tool);
    }

    public ITool? Resolve(string name)
    {
        if (_tools.TryGetValue(name, out var tool))
            return tool;

        if (_aliases.TryGetValue(name, out var canonical) && _tools.TryGetValue(canonical, out tool))
            return tool;

        return null;
    }

    public List<ToolDefinition> GetDefinitions()
    {
        return _tools.Select(t => new ToolDefinition
        {
            Name = t.Key,
            Description = t.Value.Description,
            Capability = t.Value.Capability
        }).ToList();
    }

    public bool Contains(string name)
    {
        return _tools.ContainsKey(name) || _aliases.ContainsKey(name);
    }

    public int Count => _tools.Count;
}
