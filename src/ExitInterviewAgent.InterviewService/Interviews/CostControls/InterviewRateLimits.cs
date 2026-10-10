using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Interviews.CostControls;

/// <summary>Which of the two metered actions a request is: a start, or one reply.</summary>
public enum InterviewRateKind
{
    Start,
    Reply,
}

/// <summary>
/// The per-account and per-address limits on starts and replies (web-app-plan §2, ADR-0078). It uses the framework's own
/// fixed-window limiter, the mechanism the kernel's policies use, with no queue: a refused request is answered at once.
/// A single rate-limit policy holds one partition, and these limits need two (the account and the address), so the endpoint
/// filter asks both. A fixed window gives no permit back, so an attempt counts even when the other limit refuses it.
/// </summary>
public sealed class InterviewRateLimits : IDisposable
{
    private readonly PartitionedRateLimiter<string> _startByAccount;
    private readonly PartitionedRateLimiter<string> _startByAddress;
    private readonly PartitionedRateLimiter<string> _replyByAccount;
    private readonly PartitionedRateLimiter<string> _replyByAddress;

    public InterviewRateLimits(IOptions<CostControlOptions> options)
    {
        var limits = options.Value.RateLimits;
        _startByAccount = Window(limits.StartsPerAccountPerHour, TimeSpan.FromHours(1));
        _startByAddress = Window(limits.StartsPerIpPerHour, TimeSpan.FromHours(1));
        _replyByAccount = Window(limits.RepliesPerAccountPerMinute, TimeSpan.FromMinutes(1));
        _replyByAddress = Window(limits.RepliesPerIpPerMinute, TimeSpan.FromMinutes(1));
    }

    /// <summary>Null when the attempt is admitted; otherwise the whole seconds to wait before the next attempt (at least 1).</summary>
    public int? Admit(InterviewRateKind kind, string account, string address)
    {
        var (byAccount, byAddress) = kind == InterviewRateKind.Start
            ? (_startByAccount, _startByAddress)
            : (_replyByAccount, _replyByAddress);

        using var accountLease = byAccount.AttemptAcquire(account);
        if (!accountLease.IsAcquired) return RetryAfterSeconds(accountLease);

        using var addressLease = byAddress.AttemptAcquire(address);
        return addressLease.IsAcquired ? null : RetryAfterSeconds(addressLease);
    }

    private static PartitionedRateLimiter<string> Window(int permits, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            // A zero or negative configured value would throw inside the limiter; one permit is the strictest that still works.
            PermitLimit = Math.Max(1, permits),
            Window = window,
            QueueLimit = 0,
        }));

    private static int RetryAfterSeconds(RateLimitLease lease) =>
        lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? Math.Max(1, (int)Math.Ceiling(after.TotalSeconds)) : 60;

    public void Dispose()
    {
        _startByAccount.Dispose();
        _startByAddress.Dispose();
        _replyByAccount.Dispose();
        _replyByAddress.Dispose();
    }
}
