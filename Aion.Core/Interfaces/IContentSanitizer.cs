namespace Aion.Core.Interfaces;

public enum ContentType { ConfigFile, Message, ToolOutput, MemoryEntry, LogEntry, FileWrite, CodeBlock }

public record SanitizedContent(string Content, List<string> Warnings, List<string> Errors,
    bool IsTruncated, int OriginalLength);

public interface IContentSanitizer
{
    Task<SanitizedContent> SanitizeAsync(string content, ContentType type);
}
