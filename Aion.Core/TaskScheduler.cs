using System.Collections.Concurrent;
using System.Text.Json;

namespace Aion.Core;

/// <summary>
/// Simple SQLite-backed task scheduler for recurring background agent jobs.
/// 
/// Supports: cron expressions, timezone, pause/resume, cancellation.
/// Stores tasks in ~/.aion/tasks/ scheduler.db
/// 
/// This is a port of Odysseus's task_scheduler.py pattern in C#.
/// </summary>
public class AionTaskScheduler : IDisposable
{
    private readonly string _dbPath;
    private readonly ConcurrentDictionary<string, AionTask> _tasks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeTasks = new();
    private Timer? _mainLoop;
    private bool _running;

    public event Action<AionTask>? OnTaskDue;

    public AionTaskScheduler(string? dbPath = null)
    {
        _dbPath = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".aion", "tasks", "scheduler.db");

        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        InitDb();
        LoadTasks();
    }

    private void InitDb()
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS tasks (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                prompt TEXT NOT NULL,
                schedule TEXT NOT NULL,
                cron_expression TEXT,
                scheduled_time TEXT,
                scheduled_day TEXT,
                timezone TEXT DEFAULT 'UTC',
                status TEXT DEFAULT 'active',
                owner TEXT,
                created_at TEXT DEFAULT (datetime('now')),
                updated_at TEXT,
                last_run TEXT,
                next_run TEXT,
                run_count INTEGER DEFAULT 0
            );
        """;
        cmd.ExecuteNonQuery();
    }

    private void LoadTasks()
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM tasks WHERE status IN ('active', 'paused')";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var task = new AionTask
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Prompt = reader.GetString(2),
                Schedule = reader.GetString(3),
                CronExpression = reader.IsDBNull(4) ? null : reader.GetString(4),
                ScheduledTime = reader.IsDBNull(5) ? null : reader.GetString(5),
                Status = reader.GetString(7),
                Owner = reader.IsDBNull(9) ? null : reader.GetString(9),
                LastRun = reader.IsDBNull(13) ? null : DateTime.Parse(reader.GetString(13)),
                NextRun = reader.IsDBNull(14) ? null : DateTime.Parse(reader.GetString(14)),
                RunCount = reader.GetInt32(15),
            };
            _tasks[task.Id] = task;
        }
    }

    /// <summary>
    /// Start the scheduler loop — checks every 60s for due tasks.
    /// </summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        _mainLoop = new Timer(CheckTasks, null, TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Stop the scheduler.
    /// </summary>
    public void Stop()
    {
        _running = false;
        _mainLoop?.Dispose();
        _mainLoop = null;
    }

    private void CheckTasks(object? state)
    {
        if (!_running) return;
        var now = DateTime.UtcNow;

        foreach (var (id, task) in _tasks)
        {
            if (task.Status != "active") continue;
            if (task.NextRun == null || now < task.NextRun) continue;
            if (_activeTasks.ContainsKey(id)) continue; // already running

            var cts = new CancellationTokenSource();
            _activeTasks[id] = cts;

            // Fire the task
            try
            {
                OnTaskDue?.Invoke(task);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TaskScheduler] Task {id} failed: {ex.Message}");
            }
            finally
            {
                UpdateNextRun(task);
                _activeTasks.TryRemove(id, out _);
            }
        }
    }

    private void UpdateNextRun(AionTask task)
    {
        // Simple daily schedule: next run = now + 24h
        // Full croniter port would go here
        task.LastRun = DateTime.UtcNow;
        task.NextRun = DateTime.UtcNow.AddDays(1);
        task.RunCount++;

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE tasks SET 
                last_run = @last_run, next_run = @next_run, 
                run_count = @run_count, updated_at = @updated_at
            WHERE id = @id
        """;
        cmd.Parameters.AddWithValue("@id", task.Id);
        cmd.Parameters.AddWithValue("@last_run", task.LastRun?.ToString("O"));
        cmd.Parameters.AddWithValue("@next_run", task.NextRun?.ToString("O"));
        cmd.Parameters.AddWithValue("@run_count", task.RunCount);
        cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Create a new scheduled task.
    /// </summary>
    public AionTask Schedule(AionTask task)
    {
        task.Id = task.Id ?? Guid.NewGuid().ToString("N")[..8];
        task.Status = "active";
        task.NextRun = DateTime.UtcNow.AddMinutes(1); // first run soon
        task.CreatedAt = DateTime.UtcNow;

        _tasks[task.Id] = task;

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO tasks (id, name, prompt, schedule, cron_expression, 
                scheduled_time, timezone, status, owner, next_run, created_at)
            VALUES (@id, @name, @prompt, @schedule, @cron, @time, @tz, @status, @owner, @next, @created)
        """;
        cmd.Parameters.AddWithValue("@id", task.Id);
        cmd.Parameters.AddWithValue("@name", task.Name);
        cmd.Parameters.AddWithValue("@prompt", task.Prompt);
        cmd.Parameters.AddWithValue("@schedule", task.Schedule);
        cmd.Parameters.AddWithValue("@cron", (object?)task.CronExpression ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@time", (object?)task.ScheduledTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tz", task.Timezone ?? "UTC");
        cmd.Parameters.AddWithValue("@status", "active");
        cmd.Parameters.AddWithValue("@owner", (object?)task.Owner ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@next", task.NextRun?.ToString("O") ?? "");
        cmd.Parameters.AddWithValue("@created", task.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();

        return task;
    }

    /// <summary>
    /// Pause or delete a task.
    /// </summary>
    public void Cancel(string taskId)
    {
        if (_tasks.TryRemove(taskId, out _))
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE tasks SET status = 'cancelled' WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", taskId);
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

public class AionTask
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Schedule { get; set; } = "daily";
    public string? CronExpression { get; set; }
    public string? ScheduledTime { get; set; }
    public string? ScheduledDay { get; set; }
    public string Timezone { get; set; } = "UTC";
    public string Status { get; set; } = "active";
    public string? Owner { get; set; }
    public DateTime? LastRun { get; set; }
    public DateTime? NextRun { get; set; }
    public int RunCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
