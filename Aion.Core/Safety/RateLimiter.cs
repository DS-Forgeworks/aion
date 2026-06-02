using Aion.Core.Interfaces;
using System.Collections.Concurrent;

namespace Aion.Core.Safety;

public class RateLimiter : IRateLimiter
{
    private class RateLimitRuleInternal
    {
        public int Max { get; set; }
        public int WindowMs { get; set; }
        public int Burst { get; set; }
    }

    private readonly ConcurrentDictionary<string, RateLimitRuleInternal> _rules = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _userBuckets = new();

    public void Configure(string category, int maxRequests, int windowMs, int burst = 0)
    {
        _rules[category] = new RateLimitRuleInternal
        {
            Max = maxRequests,
            WindowMs = windowMs,
            Burst = burst
        };
    }

    public Task<RateLimitResult> CheckAsync(string userId, string category)
    {
        if (!_rules.TryGetValue(category, out var rule))
        {
            return Task.FromResult(new RateLimitResult(true, int.MaxValue, 0, null));
        }

        var bucketKey = $"{userId}:{category}";
        var queue = _userBuckets.GetOrAdd(bucketKey, _ => new ConcurrentQueue<DateTime>());
        var now = DateTime.UtcNow;
        var windowStart = now.AddMilliseconds(-rule.WindowMs);

        // Clean old entries
        while (queue.TryPeek(out var oldest) && oldest < windowStart)
        {
            queue.TryDequeue(out _);
        }

        var count = queue.Count;

        if (count >= rule.Max + rule.Burst)
        {
            var oldest = queue.TryPeek(out var first) ? first : now;
            var resetMs = (long)(oldest.AddMilliseconds(rule.WindowMs) - now).TotalMilliseconds;
            return Task.FromResult(new RateLimitResult(false, Math.Max(0, rule.Max - count),
                Math.Max(0, resetMs),
                $"Rate limit exceeded: {count}/{rule.Max} requests in {rule.WindowMs / 1000}s"));
        }

        queue.Enqueue(now);
        return Task.FromResult(new RateLimitResult(true, Math.Max(0, rule.Max - count - 1), 0, null));
    }
}
