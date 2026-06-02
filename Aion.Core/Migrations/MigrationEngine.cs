using Microsoft.Data.Sqlite;

namespace Aion.Core.Migrations;

public class MigrationEngine
{
    private readonly string _connectionString;
    private const int CurrentVersion = 1;

    public MigrationEngine(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void EnsureSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL, description TEXT NOT NULL);";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
        var current = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        if (current >= CurrentVersion) return;

        string sql = @"
            CREATE TABLE IF NOT EXISTS agents (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, display_name TEXT NOT NULL,
                capabilities TEXT, capability_level INTEGER DEFAULT 1,
                status TEXT DEFAULT 'offline', last_seen TEXT, config TEXT
            );
            CREATE TABLE IF NOT EXISTS messages (
                id TEXT PRIMARY KEY, type TEXT NOT NULL, from_agent TEXT,
                to_room TEXT, to_agent TEXT, body TEXT, timestamp TEXT NOT NULL, confidence REAL
            );
            CREATE TABLE IF NOT EXISTS memory_entries (
                id TEXT PRIMARY KEY, content TEXT NOT NULL, tags TEXT,
                source TEXT, created_at TEXT NOT NULL, access_count INTEGER DEFAULT 0, embedding BLOB
            );
            CREATE TABLE IF NOT EXISTS tool_logs (
                id TEXT PRIMARY KEY, agent_id TEXT, tool_name TEXT NOT NULL,
                input TEXT, output TEXT, success INTEGER, duration_ms INTEGER, timestamp TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY, agent_id TEXT NOT NULL, user_id TEXT,
                room TEXT, started_at TEXT NOT NULL, last_activity TEXT,
                status TEXT DEFAULT 'active', message_count INTEGER DEFAULT 0, context TEXT
            );
            CREATE TABLE IF NOT EXISTS users (
                id TEXT PRIMARY KEY, email TEXT UNIQUE NOT NULL,
                display_name TEXT, role TEXT DEFAULT 'operator',
                api_key_hash TEXT NOT NULL, rate_limits TEXT, created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS plans (
                id TEXT PRIMARY KEY, session_id TEXT, agent_id TEXT NOT NULL,
                user_id TEXT, created_at TEXT NOT NULL, status TEXT NOT NULL,
                current_step INTEGER DEFAULT 0, total_steps INTEGER,
                retry_count INTEGER DEFAULT 0, error TEXT
            );
            CREATE TABLE IF NOT EXISTS plan_steps (
                plan_id TEXT, step INTEGER, tool TEXT NOT NULL,
                input TEXT, result TEXT, status TEXT DEFAULT 'pending',
                retries INTEGER DEFAULT 0, completed_at TEXT, error TEXT,
                PRIMARY KEY (plan_id, step)
            );
            INSERT INTO schema_version (version, applied_at, description) VALUES (1, datetime('now'), 'Initial schema');
        ";
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
