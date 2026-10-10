using System.Security.Cryptography;

namespace TabTower.Services.Phone;

public enum PairState { Approved, Pending, Denied, Busy }

/// <summary>A device asking to be let in, waiting for the user to answer on the PC.</summary>
public sealed record PendingPairing(DeviceInfo Device, string Code, DateTime At);

/// <summary>
/// Which tailnet devices may use the phone page. A device is identified by its Tailscale node
/// StableID, which is bound to that device's WireGuard key: a second machine logged in as the
/// same user is a different node and has to be approved on its own. Nothing secret is ever
/// handed to the phone, so there is nothing on it to steal or leak.
///
/// An unknown device gets a pending request with a short code that only the DEVICE shows. The PC
/// shows the device's names and approves only when the user types that code: a device name is
/// chosen by whoever controls the device, so another node of the same user calling itself
/// "iPhone" at the same moment cannot be approved by picking the wrong prompt. Approval lasts
/// until revoked. A denied device cannot raise a new prompt for a while, and at most
/// <see cref="MaxPending"/> requests wait at once, so nothing can flood the PC with prompts.
///
/// Thread-safe: requests arrive on the thread pool, answers come from the UI thread.
/// </summary>
public sealed class PhonePairing
{
    public static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DenyCooldown = TimeSpan.FromMinutes(10);
    public const int MaxPending = 2;
    // No 0/O, 1/I/L, 2/Z, 5/S, 8/B: read aloud or off a small screen, they get confused.
    private const string CodeAlphabet = "ACDEFHJKMNPRTUVWXY34679";

    private readonly object _gate = new();
    private readonly List<PairedDevice> _approved;
    private readonly Dictionary<string, PendingPairing> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _deniedUntil = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _now;

    /// <summary>A new pending request appeared. Raised once per request, outside the lock.</summary>
    public event Action<PendingPairing>? Requested;

    /// <summary>The approved list changed (approve, revoke, or a device seen again).</summary>
    public event Action? ApprovedChanged;

    public PhonePairing(IEnumerable<PairedDevice>? approved = null, Func<DateTime>? now = null)
    {
        _approved = approved?.Where(d => d.StableId.Length > 0).ToList() ?? new List<PairedDevice>();
        _now = now ?? (() => DateTime.Now);
    }

    public IReadOnlyList<PairedDevice> Approved
    {
        get { lock (_gate) return _approved.Select(Copy).ToList(); }
    }

    public IReadOnlyList<PendingPairing> Pending
    {
        get { lock (_gate) { Expire(); return _pending.Values.ToList(); } }
    }

    /// <summary>Where this device stands. An unknown device gets a pending request (and the
    /// prompt) the first time it is seen; the code is returned for the page to show.</summary>
    public (PairState State, string Code) Check(DeviceInfo device)
    {
        PendingPairing? raised = null;
        (PairState, string) result;
        bool seen = false;
        lock (_gate)
        {
            Expire();
            var known = _approved.FirstOrDefault(d => d.StableId == device.StableId);
            if (known != null)
            {
                // Remembered so the settings list can show which devices are actually in use.
                // Saved at most once an hour per device; it is a convenience, not an audit log.
                if (known.LastSeenAt is not { } last || _now() - last > TimeSpan.FromHours(1))
                {
                    known.LastSeenAt = _now();
                    seen = true;
                }
                if (device.Name.Length > 0 && known.Name != device.Name) { known.Name = device.Name; seen = true; }
                result = (PairState.Approved, "");
            }
            else if (_deniedUntil.TryGetValue(device.StableId, out var until) && until > _now())
                result = (PairState.Denied, "");
            else if (_pending.TryGetValue(device.StableId, out var p))
                result = (PairState.Pending, p.Code);
            else if (_pending.Count >= MaxPending)
                result = (PairState.Busy, "");
            else
            {
                raised = new PendingPairing(device, NewCode(), _now());
                _pending[device.StableId] = raised;
                result = (PairState.Pending, raised.Code);
            }
        }
        if (raised != null) Requested?.Invoke(raised);
        if (seen) ApprovedChanged?.Invoke();
        return result;
    }

    /// <summary>Approve a pending device with the code the user typed, as read off that device.
    /// False when the code does not match (the request stays pending, to be typed again or
    /// denied) or the request is gone (expired, or answered elsewhere).</summary>
    public bool Approve(string stableId, string typedCode)
    {
        lock (_gate)
        {
            Expire();
            if (!_pending.TryGetValue(stableId, out var p)) return false;
            if (!string.Equals(p.Code, (typedCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            _pending.Remove(stableId);
            _deniedUntil.Remove(stableId);
            _approved.RemoveAll(d => d.StableId == stableId);
            _approved.Add(new PairedDevice
            {
                StableId = stableId, Name = p.Device.Name, Os = p.Device.Os,
                ApprovedAt = _now(), LastSeenAt = _now(),
            });
        }
        ApprovedChanged?.Invoke();
        return true;
    }

    public void Deny(string stableId)
    {
        lock (_gate)
        {
            _pending.Remove(stableId);
            _deniedUntil[stableId] = _now() + DenyCooldown;
        }
    }

    /// <summary>Remove an approved device. Its next request starts a new pairing.</summary>
    public bool Revoke(string stableId)
    {
        bool removed;
        lock (_gate) removed = _approved.RemoveAll(d => d.StableId == stableId) > 0;
        if (removed) ApprovedChanged?.Invoke();
        return removed;
    }

    public bool IsPending(string stableId)
    {
        lock (_gate) { Expire(); return _pending.ContainsKey(stableId); }
    }

    private void Expire()
    {
        var now = _now();
        foreach (var id in _pending.Where(kv => now - kv.Value.At > PendingTtl).Select(kv => kv.Key).ToList())
            _pending.Remove(id);
        foreach (var id in _deniedUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
            _deniedUntil.Remove(id);
    }

    private static string NewCode()
    {
        Span<char> c = stackalloc char[4];
        for (int i = 0; i < c.Length; i++) c[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(c);
    }

    private static PairedDevice Copy(PairedDevice d) => new()
    {
        StableId = d.StableId, Name = d.Name, Os = d.Os, ApprovedAt = d.ApprovedAt, LastSeenAt = d.LastSeenAt,
    };
}
