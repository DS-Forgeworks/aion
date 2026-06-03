using Aion.Core.Interfaces;
using Aion.Core.Models;
using Microsoft.Data.Sqlite;

namespace Aion.Core.Memory;

public class SqliteConversationStore : IConversationStore
{
    private readonly string _connectionString;

    public SqliteConversationStore(string connectionString)
    {
        _connectionString = connectionString;
        EnsureTable();
    }

    private void EnsureTable()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS conversations (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL DEFAULT 'New Conversation',
                agent_id TEXT NOT NULL DEFAULT 'default',
                user_id TEXT,
                model TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                message_count INTEGER NOT NULL DEFAULT 0,
                pinned INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS chat_messages (
                id TEXT PRIMARY KEY,
                conversation_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                model TEXT,
                created_at TEXT NOT NULL,
                edited INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (conversation_id) REFERENCES conversations(id)
            );
            CREATE INDEX IF NOT EXISTS idx_messages_conv ON chat_messages(conversation_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_conv_updated ON conversations(updated_at);
        ";
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection GetConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task<Conversation> CreateConversationAsync(string title, string agentId, string? userId = null, string? model = null)
    {
        var conv = new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            AgentId = agentId,
            UserId = userId,
            Model = model,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO conversations (id, title, agent_id, user_id, model, created_at, updated_at, message_count, pinned)
            VALUES ($id, $title, $agent, $user, $model, $created, $updated, 0, 0)";
        cmd.Parameters.AddWithValue("$id", conv.Id);
        cmd.Parameters.AddWithValue("$title", conv.Title);
        cmd.Parameters.AddWithValue("$agent", conv.AgentId);
        cmd.Parameters.AddWithValue("$user", (object?)conv.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$model", (object?)conv.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", conv.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", conv.UpdatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync();

        return conv;
    }

    public async Task<List<Conversation>> ListConversationsAsync(string? agentId = null, int limit = 50)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = agentId != null
            ? "SELECT * FROM conversations WHERE agent_id = $agent ORDER BY updated_at DESC LIMIT $limit"
            : "SELECT * FROM conversations ORDER BY updated_at DESC LIMIT $limit";
        if (agentId != null) cmd.Parameters.AddWithValue("$agent", agentId);
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<Conversation>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadConversation(reader));
        }
        return list;
    }

    public async Task<Conversation?> GetConversationAsync(string id)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM conversations WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return ReadConversation(reader);
        return null;
    }

    public async Task<bool> UpdateConversationAsync(Conversation conversation)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE conversations SET title = $title, agent_id = $agent, model = $model,
                updated_at = $updated, message_count = $count, pinned = $pinned
            WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", conversation.Id);
        cmd.Parameters.AddWithValue("$title", conversation.Title);
        cmd.Parameters.AddWithValue("$agent", conversation.AgentId);
        cmd.Parameters.AddWithValue("$model", (object?)conversation.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$count", conversation.MessageCount);
        cmd.Parameters.AddWithValue("$pinned", conversation.Pinned ? 1 : 0);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteConversationAsync(string id)
    {
        using var conn = GetConnection();
        using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "DELETE FROM chat_messages WHERE conversation_id = $id";
        cmd1.Parameters.AddWithValue("$id", id);
        await cmd1.ExecuteNonQueryAsync();

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "DELETE FROM conversations WHERE id = $id";
        cmd2.Parameters.AddWithValue("$id", id);
        return await cmd2.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> PinConversationAsync(string id, bool pinned)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE conversations SET pinned = $pinned WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$pinned", pinned ? 1 : 0);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<ChatMessage> AddMessageAsync(string conversationId, string role, string content, string? model = null)
    {
        var msg = new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            Model = model,
            CreatedAt = DateTime.UtcNow
        };

        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO chat_messages (id, conversation_id, role, content, model, created_at, edited)
            VALUES ($id, $conv, $role, $content, $model, $created, 0);
            UPDATE conversations SET message_count = message_count + 1, updated_at = $updated WHERE id = $conv";
        cmd.Parameters.AddWithValue("$id", msg.Id);
        cmd.Parameters.AddWithValue("$conv", conversationId);
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.Parameters.AddWithValue("$model", (object?)model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", msg.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();

        return msg;
    }

    public async Task<List<ChatMessage>> GetMessagesAsync(string conversationId, int limit = 100)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM chat_messages WHERE conversation_id = $conv ORDER BY created_at ASC LIMIT $limit";
        cmd.Parameters.AddWithValue("$conv", conversationId);
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<ChatMessage>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ChatMessage
            {
                Id = reader.GetString(0),
                ConversationId = reader.GetString(1),
                Role = reader.GetString(2),
                Content = reader.GetString(3),
                Model = reader.IsDBNull(4) ? null : reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                Edited = reader.GetInt32(6) == 1
            });
        }
        return list;
    }

    public async Task<bool> EditMessageAsync(string messageId, string newContent)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE chat_messages SET content = $content, edited = 1 WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", messageId);
        cmd.Parameters.AddWithValue("$content", newContent);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteMessageAsync(string messageId)
    {
        using var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM chat_messages WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", messageId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static Conversation ReadConversation(SqliteDataReader reader)
    {
        return new Conversation
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            AgentId = reader.GetString(2),
            UserId = reader.IsDBNull(3) ? null : reader.GetString(3),
            Model = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAt = DateTime.Parse(reader.GetString(5)),
            UpdatedAt = DateTime.Parse(reader.GetString(6)),
            MessageCount = reader.GetInt32(7),
            Pinned = reader.GetInt32(8) == 1
        };
    }
}
