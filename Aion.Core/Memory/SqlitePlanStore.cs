using Aion.Core.Interfaces;
using Microsoft.Data.Sqlite;

namespace Aion.Core.Memory;

public class SqlitePlanStore : IPlanStore
{
    private readonly string _connectionString;

    public SqlitePlanStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<Plan> CreateAsync(Plan plan)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO plans (id, session_id, agent_id, user_id, created_at, status, current_step, total_steps, retry_count, error)
            VALUES (@id, @sid, @aid, @uid, @created, @status, @cs, @total, @retry, @err)";
        cmd.Parameters.AddWithValue("@id", plan.Id);
        cmd.Parameters.AddWithValue("@sid", plan.SessionId);
        cmd.Parameters.AddWithValue("@aid", plan.AgentId);
        cmd.Parameters.AddWithValue("@uid", plan.UserId);
        cmd.Parameters.AddWithValue("@created", plan.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@status", plan.Status);
        cmd.Parameters.AddWithValue("@cs", plan.CurrentStep);
        cmd.Parameters.AddWithValue("@total", plan.TotalSteps);
        cmd.Parameters.AddWithValue("@retry", plan.RetryCount);
        cmd.Parameters.AddWithValue("@err", (object?)plan.Error ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();

        // Insert steps
        foreach (var step in plan.Steps)
        {
            cmd.CommandText = @"
                INSERT INTO plan_steps (plan_id, step, tool, input, result, status, retries, completed_at, error)
                VALUES (@pid, @step, @tool, @input, @result, @status, @retries, @ca, @err)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@pid", plan.Id);
            cmd.Parameters.AddWithValue("@step", step.Step);
            cmd.Parameters.AddWithValue("@tool", step.Tool);
            cmd.Parameters.AddWithValue("@input", step.Input);
            cmd.Parameters.AddWithValue("@result", (object?)step.Result ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@status", step.Status);
            cmd.Parameters.AddWithValue("@retries", step.Retries);
            cmd.Parameters.AddWithValue("@ca", (object?)step.CompletedAt?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@err", (object?)step.Error ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        return plan;
    }

    public async Task<Plan?> GetAsync(string planId)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM plans WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", planId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var plan = new Plan(
            reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            DateTime.Parse(reader.GetString(4)), reader.GetString(5),
            reader.GetInt32(6), reader.GetInt32(7),
            reader.GetInt32(8), reader.IsDBNull(9) ? null : reader.GetString(9),
            new List<PlanStep>()
        );

        reader.Close();

        // Load steps
        cmd.CommandText = "SELECT * FROM plan_steps WHERE plan_id = @id ORDER BY step";
        using var stepReader = await cmd.ExecuteReaderAsync();
        while (await stepReader.ReadAsync())
        {
            plan.Steps.Add(new PlanStep(
                stepReader.GetInt32(1), stepReader.GetString(2),
                stepReader.GetString(3),
                stepReader.IsDBNull(4) ? null : stepReader.GetString(4),
                stepReader.GetString(6), stepReader.GetInt32(7),
                stepReader.IsDBNull(8) ? null : DateTime.Parse(stepReader.GetString(8)),
                stepReader.IsDBNull(9) ? null : stepReader.GetString(9)
            ));
        }

        return plan;
    }

    public async Task<bool> UpdateStepAsync(string planId, int step, string status, string? result, string? error)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE plan_steps SET status = @status, result = @result, 
            error = @err, completed_at = CASE WHEN @status = 'completed' THEN datetime('now') ELSE completed_at END
            WHERE plan_id = @pid AND step = @step";
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@result", (object?)result ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@err", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pid", planId);
        cmd.Parameters.AddWithValue("@step", step);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> UpdateStatusAsync(string planId, string status, string? error)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE plans SET status = @status, error = @err WHERE id = @id";
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@err", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", planId);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<Plan>> GetActivePlansAsync(string agentId)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM plans WHERE agent_id = @aid AND status IN ('in_progress', 'pending')";
        cmd.Parameters.AddWithValue("@aid", agentId);

        var ids = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetString(0));

        var plans = new List<Plan>();
        foreach (var id in ids)
        {
            var plan = await GetAsync(id);
            if (plan != null) plans.Add(plan);
        }

        return plans;
    }
}
