using Aion.Core.Interfaces;

namespace Aion.Core.Models;

public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Parameters { get; set; } // JSON Schema string
    public ToolCapability Capability { get; set; } = ToolCapability.ReadOnly;
}
