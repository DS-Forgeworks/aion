namespace Aion.Core.Interfaces;

public record RateLimitResult(bool Allowed, int Remaining, long ResetMs, string? Error);

public interface IRateLimiter
{
    Task<RateLimitResult> CheckAsync(string userId, string category);
    void Configure(string category, int maxRequests, int windowMs, int burst = 0);
}
