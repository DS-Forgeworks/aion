namespace Aion.Core.Interfaces;

public record MemoryEntry(string Id, string Content, string? Tags, string? Source,
    DateTime CreatedAt, int AccessCount = 0, byte[]? Embedding = null);

public interface IMemoryStore
{
    Task StoreAsync(MemoryEntry entry);
    Task<List<MemoryEntry>> SearchAsync(string query, int topK = 5);
    Task<List<MemoryEntry>> GetRecentAsync(int count = 10);
    Task<bool> DeleteAsync(string id);
}
