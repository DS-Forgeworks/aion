using System.Collections.Concurrent;
using System.Text;

namespace Aion.Core;

/// <summary>
/// Streaming progress emitter for long-running tool execution.
/// Inspired by Odysseus's subprocess progress — pushes tail-of-output 
/// every N seconds so the frontend has something to show.
/// 
/// Usage:
///   var emitter = new ProgressEmitter("shell");
///   _ = emitter.StartAsync(onProgress);
///   // ... run tool ...
///   emitter.AppendOutput(line);
///   // ... done ...
///   await emitter.StopAsync();
/// </summary>
public class ProgressEmitter : IDisposable
{
    private readonly string _toolName;
    private readonly int _intervalMs;
    private readonly int _tailLines;
    private readonly ConcurrentQueue<string> _output = new();
    private CancellationTokenSource? _cts;
    private Task? _emitterTask;

    public ProgressEmitter(string toolName, int intervalMs = 2000, int tailLines = 12)
    {
        _toolName = toolName;
        _intervalMs = intervalMs;
        _tailLines = tailLines;
    }

    /// <summary>
    /// Called each interval with current progress state.
    /// </summary>
    public event Action<ProgressEvent>? OnProgress;

    /// <summary>
    /// Start the emitter background loop.
    /// </summary>
    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        
        _emitterTask = Task.Run(async () =>
        {
            // Skip first interval — fast tools don't need progress noise
            await Task.Delay(_intervalMs, token);
            
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var tail = GetTail();
                    OnProgress?.Invoke(new ProgressEvent
                    {
                        ToolName = _toolName,
                        Tail = tail,
                        Timestamp = DateTime.UtcNow,
                    });
                    await Task.Delay(_intervalMs, token);
                }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    /// <summary>
    /// Append output line (called by tool execution).
    /// </summary>
    public void AppendOutput(string line)
    {
        _output.Enqueue(line);
        // Keep the queue from growing unbounded
        while (_output.Count > _tailLines * 4)
            _output.TryDequeue(out _);
    }

    /// <summary>
    /// Get the most recent N lines of output.
    /// </summary>
    public List<string> GetTail()
    {
        // Snapshot the queue (newest items at the end)
        var all = _output.ToArray();
        var skip = Math.Max(0, all.Length - _tailLines);
        return all.Skip(skip).ToList();
    }

    /// <summary>
    /// Stop the emitter. Returns final output tail.
    /// </summary>
    public async Task<List<string>> StopAsync()
    {
        _cts?.Cancel();
        if (_emitterTask != null)
        {
            try { await _emitterTask; } catch (OperationCanceledException) { }
        }
        return GetTail();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

public class ProgressEvent
{
    public string ToolName { get; set; } = "";
    public List<string> Tail { get; set; } = new();
    public DateTime Timestamp { get; set; }
}
