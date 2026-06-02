namespace Aion.Core.Models;

public class Session
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Room { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastActivity { get; set; }
    public string Status { get; set; } = "active";
    public int MessageCount { get; set; }
    public string? Context { get; set; }
}
