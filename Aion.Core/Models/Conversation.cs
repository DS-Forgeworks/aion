using Aion.Core.Interfaces;

namespace Aion.Core.Models;

public class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Conversation";
    public string AgentId { get; set; } = "default";
    public string? UserId { get; set; }
    public string? Model { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int MessageCount { get; set; }
    public bool Pinned { get; set; }
}

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = string.Empty;
    public string Role { get; set; } = "user";  // "user" | "assistant" | "system"
    public string Content { get; set; } = string.Empty;
    public string? Model { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Edited { get; set; }
}
