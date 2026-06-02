namespace Aion.Core.Models;

public class AppConfig
{
    public int Version { get; set; } = 1;
    public LlmConfig Llm { get; set; } = new();
    public string Language { get; set; } = "en";
    public AgentConfig Agent { get; set; } = new();
    public SafetyConfig Safety { get; set; } = new();
    public string Workspace { get; set; } = "~/.aion/workspaces/luna";
    public MeshConfig Mesh { get; set; } = new();
    public MemoryConfig Memory { get; set; } = new();
    public RateLimitConfig RateLimits { get; set; } = new();
}

public class LlmConfig
{
    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "qwen3:8b";
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
}

public class AgentConfig
{
    public string Name { get; set; } = "default";
    public string Template { get; set; } = "default";
}

public class SafetyConfig
{
    public bool SafeMode { get; set; } = true;
    public bool ShellEnabled { get; set; } = false;
    public List<string> FileWriteAllowed { get; set; } = new();
}

public class MeshConfig
{
    public bool Enabled { get; set; } = false;
    public int Port { get; set; } = 6970;
}

public class MemoryConfig
{
    public string? EmbeddingModel { get; set; }
    public string EmbeddingDevice { get; set; } = "cpu";
}

public class RateLimitConfig
{
    public RateLimitRule ToolCalls { get; set; } = new() { Max = 10, WindowMs = 30000 };
    public RateLimitRule LlmRequests { get; set; } = new() { Max = 30, WindowMs = 60000, Burst = 5 };
    public RateLimitRule MeshMessages { get; set; } = new() { Max = 20, WindowMs = 10000 };
    public int ConcurrentTasks { get; set; } = 3;
}

public class RateLimitRule
{
    public int Max { get; set; }
    public int WindowMs { get; set; }
    public int Burst { get; set; } = 0;
}
