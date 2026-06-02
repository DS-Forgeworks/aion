using Aion.Core.Interfaces;
using Microsoft.Data.Sqlite;

namespace Aion.Core.Memory;

public class SqliteMemoryStore : IMemoryStore
{
    private readonly string _connectionString;

    public SqliteMemoryStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task StoreAsync(MemoryEntry entry)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO memory_entries 
            (id, content, tags, source, created_at, access_count, embedding)
            VALUES (@id, @content, @tags, @source, @created_at, @access_count, @embedding)";

        cmd.Parameters.AddWithValue("@id", entry.Id);
        cmd.Parameters.AddWithValue("@content", entry.Content);
        cmd.Parameters.AddWithValue("@tags", entry.Tags ?? "");
        cmd.Parameters.AddWithValue("@source", entry.Source ?? "");
        cmd.Parameters.AddWithValue("@created_at", entry.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@access_count", entry.AccessCount);
        cmd.Parameters.AddWithValue("@embedding", entry.Embedding ?? Array.Empty<byte>());

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<MemoryEntry>> SearchAsync(string query, int topK = 5)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // FTS5 search
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, content, tags, source, created_at, access_count
            FROM memory_entries
            WHERE content LIKE @query
            ORDER BY created_at DESC
            LIMIT @topK";

        cmd.Parameters.AddWithValue("@query", $"%{query}%");
        cmd.Parameters.AddWithValue("@topK", topK);

        var results = new List<MemoryEntry>();
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new MemoryEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                DateTime.Parse(reader.GetString(4)),
                reader.GetInt32(5)
            ));
        }

        return results;
    }

    public async Task<List<MemoryEntry>> GetRecentAsync(int count = 10)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, content, tags, source, created_at, access_count FROM memory_entries ORDER BY created_at DESC LIMIT @count";
        cmd.Parameters.AddWithValue("@count", count);

        var results = new List<MemoryEntry>();
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new MemoryEntry(
                reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                DateTime.Parse(reader.GetString(4)),
                reader.GetInt32(5)
            ));
        }

        return results;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM memory_entries WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
