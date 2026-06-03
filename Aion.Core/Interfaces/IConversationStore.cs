using Aion.Core.Models;

namespace Aion.Core.Interfaces;

public interface IConversationStore
{
    Task<Conversation> CreateConversationAsync(string title, string agentId, string? userId = null, string? model = null);
    Task<List<Conversation>> ListConversationsAsync(string? agentId = null, int limit = 50);
    Task<Conversation?> GetConversationAsync(string id);
    Task<bool> UpdateConversationAsync(Conversation conversation);
    Task<bool> DeleteConversationAsync(string id);
    Task<bool> PinConversationAsync(string id, bool pinned);

    Task<ChatMessage> AddMessageAsync(string conversationId, string role, string content, string? model = null);
    Task<List<ChatMessage>> GetMessagesAsync(string conversationId, int limit = 100);
    Task<bool> EditMessageAsync(string messageId, string newContent);
    Task<bool> DeleteMessageAsync(string messageId);
}
