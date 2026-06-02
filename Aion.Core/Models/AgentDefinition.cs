using Aion.Core.Interfaces;

namespace Aion.Core.Models;

public class AgentDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Capabilities { get; set; } = new();
    public ToolCapability CapabilityLevel { get; set; } = ToolCapability.ReadOnly;
    public string Status { get; set; } = "offline";
    public DateTime LastSeen { get; set; }
    public string? Config { get; set; }
}
