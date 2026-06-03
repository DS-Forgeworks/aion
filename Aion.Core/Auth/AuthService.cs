using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Aion.Core.Auth;

public class AuthService
{
    private readonly string _connectionString;

    public AuthService(string connectionString)
    {
        _connectionString = connectionString;
        EnsureTable();
    }

    private void EnsureTable()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Add password_hash column if not exists (migration engine creates users without this)
        using var c1 = conn.CreateCommand();
        c1.CommandText = "SELECT COUNT(*) FROM pragma_table_info('users') WHERE name='password_hash'";
        if ((long)c1.ExecuteScalar()! == 0)
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE users ADD COLUMN password_hash TEXT";
            alter.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS user_sessions (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                token TEXT UNIQUE NOT NULL,
                expires_at TEXT NOT NULL,
                FOREIGN KEY (user_id) REFERENCES users(id)
            );
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
        ";
        cmd.ExecuteNonQuery();

        // Create default admin if no users exist
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM users";
        if ((long)check.ExecuteScalar()! == 0)
        {
            using var create = conn.CreateCommand();
            create.CommandText = @"
                INSERT INTO users (id, email, display_name, role, password_hash, api_key_hash, created_at)
                VALUES ($id, 'admin', 'Admin', 'admin', $hash, '', $now)";
            create.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            create.Parameters.AddWithValue("$hash", HashPassword("aion"));
            create.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            create.ExecuteNonQuery();
        }
    }

    public async Task<(bool success, string? token, string? error)> LoginAsync(string username, string password)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, password_hash FROM users WHERE email = $user";
        cmd.Parameters.AddWithValue("$user", username);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (false, null, "Invalid username or password");

        var userId = reader.GetString(0);
        var hash = reader.IsDBNull(1) ? null : reader.GetString(1);

        if (hash != HashPassword(password))
            return (false, null, "Invalid username or password");

        // Create session
        var token = GenerateToken();
        using var sessionCmd = conn.CreateCommand();
        sessionCmd.CommandText = @"
            INSERT INTO user_sessions (id, user_id, token, expires_at)
            VALUES ($id, $user, $token, $expires)";
        sessionCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        sessionCmd.Parameters.AddWithValue("$user", userId);
        sessionCmd.Parameters.AddWithValue("$token", token);
        sessionCmd.Parameters.AddWithValue("$expires", DateTime.UtcNow.AddDays(30).ToString("O"));
        await sessionCmd.ExecuteNonQueryAsync();

        return (true, token, null);
    }

    public async Task<string?> ValidateTokenAsync(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            // Check for API key
            var apiKey = await GetApiKeyAsync();
            if (apiKey != null && token == apiKey) return "api";
            return null;
        }

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT user_id FROM user_sessions 
            WHERE token = $token AND expires_at > $now";
        cmd.Parameters.AddWithValue("$token", token);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    public async Task<bool> LogoutAsync(string token)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM user_sessions WHERE token = $token";
        cmd.Parameters.AddWithValue("$token", token);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> ChangePasswordAsync(string token, string oldPassword, string newPassword)
    {
        var userId = await ValidateTokenAsync(token);
        if (userId == null) return false;

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE users SET password_hash = $hash WHERE id = $id AND password_hash = $old";
        cmd.Parameters.AddWithValue("$id", userId);
        cmd.Parameters.AddWithValue("$hash", HashPassword(newPassword));
        cmd.Parameters.AddWithValue("$old", HashPassword(oldPassword));
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // First run detection: check if default password is still in use
    public async Task<bool> IsFirstRunAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE email = 'admin' AND password_hash = $hash";
        cmd.Parameters.AddWithValue("$hash", HashPassword("aion"));
        return (long)cmd.ExecuteScalar()! > 0;
    }

    public async Task<string?> GetApiKeyAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_settings WHERE key = 'api_key'";
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    public async Task SetApiKeyAsync(string key)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO app_settings (key, value) VALUES ('api_key', $key)";
        cmd.Parameters.AddWithValue("$key", key);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
