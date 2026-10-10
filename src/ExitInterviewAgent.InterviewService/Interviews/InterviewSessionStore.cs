using ExitInterviewAgent.Records;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Interviews;

public enum SessionLookup { Found, Gone, NotFound }

/// <summary>
/// The sessions of this process, in memory only (ADR-0076). One open session per account; ids are random and bound to their
/// account, so another account sees 404. Expired sessions are wiped and leave a short tombstone (id and owner only), so that a
/// late request gets 410 rather than 404. Cancellation of a wiped session happens outside the lock.
/// </summary>
public sealed class InterviewSessionStore(TimeProvider clock, IOptionsMonitor<InterviewServiceOptions> options)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, InterviewSession> _sessions = [];
    private readonly Dictionary<string, string> _openByOwner = [];
    private readonly Dictionary<string, (string Owner, DateTimeOffset Until)> _tombstones = [];

    /// <summary>Adds the session unless its account already has an open one. Returns false for a second open session.</summary>
    public bool TryAdd(InterviewSession session)
    {
        lock (_gate)
        {
            if (_openByOwner.ContainsKey(session.Owner)) return false;
            _sessions[session.Id] = session;
            _openByOwner[session.Owner] = session.Id;
            return true;
        }
    }

    /// <summary>Finds the account's session. An expired one is wiped here, so the caller gets <see cref="SessionLookup.Gone"/>.</summary>
    public SessionLookup Find(string owner, string id, out InterviewSession? session)
    {
        var now = clock.GetUtcNow();
        InterviewSession? expired = null;
        lock (_gate)
        {
            PurgeTombstonesLocked(now);
            if (_sessions.TryGetValue(id, out var found) && found.Owner == owner)
            {
                if (found.IsExpired(now, options.CurrentValue))
                {
                    expired = RemoveLocked(found);
                    _tombstones[id] = (owner, now + options.CurrentValue.IdleTimeout);
                    session = null;
                }
                else
                {
                    session = found;
                    return SessionLookup.Found;
                }
            }
            else if (_tombstones.TryGetValue(id, out var tomb) && tomb.Owner == owner)
            {
                session = null;
                return SessionLookup.Gone;
            }
            else
            {
                session = null;
                return SessionLookup.NotFound;
            }
        }
        expired!.Cancel();
        return SessionLookup.Gone;
    }

    /// <summary>Wipes the account's session on delete. Returns false when there is none to wipe.</summary>
    public bool Remove(string owner, string id)
    {
        InterviewSession? removed;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out var found) || found.Owner != owner) return false;
            removed = RemoveLocked(found);
        }
        removed.Cancel();
        return true;
    }

    /// <summary>Called once a session has ended: the account may start another one.</summary>
    public void MarkClosed(string owner, string id)
    {
        lock (_gate)
        {
            if (_openByOwner.TryGetValue(owner, out var open) && open == id) _openByOwner.Remove(owner);
        }
    }

    /// <summary>The background sweep: wipes every expired session and tombstone.</summary>
    public int Sweep()
    {
        var now = clock.GetUtcNow();
        List<InterviewSession> expired = [];
        lock (_gate)
        {
            foreach (var session in _sessions.Values.Where(s => s.IsExpired(now, options.CurrentValue)).ToList())
            {
                expired.Add(RemoveLocked(session));
                _tombstones[session.Id] = (session.Owner, now + options.CurrentValue.IdleTimeout);
            }
            PurgeTombstonesLocked(now);
        }
        foreach (var session in expired) session.Cancel();
        return expired.Count;
    }

    public int Count { get { lock (_gate) return _sessions.Count; } }

    private InterviewSession RemoveLocked(InterviewSession session)
    {
        _sessions.Remove(session.Id);
        if (_openByOwner.TryGetValue(session.Owner, out var open) && open == session.Id) _openByOwner.Remove(session.Owner);
        return session;
    }

    private void PurgeTombstonesLocked(DateTimeOffset now)
    {
        foreach (var (id, tomb) in _tombstones.Where(t => now >= t.Value.Until).ToList()) _tombstones.Remove(id);
    }
}
