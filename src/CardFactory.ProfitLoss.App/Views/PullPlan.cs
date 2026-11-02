using System.Diagnostics;

namespace CardFactory.ProfitLoss.App.Views;

/// <summary>One report in a pull: how to pull it on each sign-in, and its result.</summary>
internal sealed class PlannedPull
{
    public PlannedPull(string name, double seconds, int priority,
        Func<Task<string>> onOwn, Func<FlooidView, Task<string>> onSecond, Func<bool>? stillNeeded = null)
    {
        Name = name;
        Seconds = seconds;
        Priority = priority;
        OnOwn = onOwn;
        OnSecond = onSecond;
        StillNeeded = stillNeeded;
    }

    public string Name { get; }

    /// <summary>About how long it takes (till log, 2 Nov 2026, Fast): the longest go first.</summary>
    public double Seconds { get; }

    /// <summary>0 for today's reports; 1 for last year's, which are only wanted at the end.</summary>
    public int Priority { get; }

    public Func<Task<string>> OnOwn { get; }
    public Func<FlooidView, Task<string>> OnSecond { get; }

    /// <summary>Asked when it comes up: false skips it (its result is then null).</summary>
    public Func<bool>? StillNeeded { get; }

    public DateTime StartedAt { get; set; }
    public string PulledBy { get; set; } = string.Empty;

    /// <summary>Where the result is kept for the next pull (today's reports); null for none.</summary>
    public (string Report, DateTime From, DateTime To)? Keep { get; init; }

    /// <summary>The report's page; null when skipped. Faults when the pull failed, cancelled
    /// when the pull was stopped before it started.</summary>
    public TaskCompletionSource<string?> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Two sign-ins (owner, 2 Nov 2026: "optimise the pulls between the two sign-ins so it's the
/// fastest possible"). All the reports a pull needs go on one list, longest first; the app's own
/// sign-in and the second sign-in each take the next report as soon as they are free. That is
/// the quickest split for any mix of reports, and it needs no fixed shares: whichever reports
/// the second sign-in does not get to (not signed in yet, not in use, or failed) the app's own
/// simply takes. A report that fails on the second sign-in goes back on the list.
///
/// Times on the till (2 Nov 2026, Fast): Gift Cards 6.4 s, Stamps 6.2, Refunds 2.4, Branch 2.3,
/// Staff Discounts 2.2. With both signed in, Refresh (three reports) ≈ 6.4 s - Gift Cards on one,
/// Refunds and Branch on the other - against 11 s on one sign-in.
///
/// Everything runs on the window's thread, as the pulls always have, so no locking is needed.
/// </summary>
internal sealed class PullPlan
{
    private readonly string _label;
    private readonly List<PlannedPull> _all;
    private readonly List<PlannedPull> _waiting;
    private readonly Action<PlannedPull, string> _stored;
    private readonly Func<bool>? _stopWhen;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _stopped;

    public PullPlan(string label, IEnumerable<PlannedPull> pulls, Action<PlannedPull, string> stored, Func<bool>? stopWhen = null)
    {
        _label = label;
        _all = pulls.ToList();
        _waiting = _all.OrderBy(p => p.Priority).ThenByDescending(p => p.Seconds).ToList();
        _stored = stored;
        _stopWhen = stopWhen;
    }

    public bool IsEmpty => _all.Count == 0;

    private bool Stopping => _stopped || (_stopWhen?.Invoke() ?? false);

    private bool AllDone => _all.All(p => p.Done.Task.IsCompleted);

    /// <summary>Nothing more is started; reports already being pulled finish.</summary>
    public void Stop()
    {
        _stopped = true;
        CancelWaiting();
    }

    private void CancelWaiting()
    {
        foreach (var p in _waiting) p.Done.TrySetCanceled();
        _waiting.Clear();
        Signal();
    }

    private void Signal()
    {
        var changed = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.TrySetResult();
    }

    private bool TryTake(out PlannedPull pull)
    {
        if (Stopping)
        {
            CancelWaiting();
            pull = null!;
            return false;
        }
        while (_waiting.Count > 0)
        {
            var next = _waiting[0];
            _waiting.RemoveAt(0);
            if (next.StillNeeded is null || next.StillNeeded())
            {
                pull = next;
                return true;
            }
            next.Done.TrySetResult(null);   // not needed after all
        }
        pull = null!;
        return false;
    }

    private void PutBack(PlannedPull pull)
    {
        if (Stopping) { pull.Done.TrySetCanceled(); Signal(); return; }
        _waiting.Insert(0, pull);
        Signal();
    }

    private void Complete(PlannedPull pull, string html)
    {
        try { _stored(pull, html); } catch { }
        pull.Done.TrySetResult(html);
        Signal();
    }

    /// <summary>Runs the plan on both sign-ins; finishes when every report is done.</summary>
    public async Task RunAsync(SecondSignIn second, SecondSignIn.Details? details)
    {
        if (IsEmpty) return;
        var clock = Stopwatch.StartNew();
        // Not waited for: the second sign-in may still be signing in when the app's own has
        // pulled everything, and the pull must not wait for that. Once it is free it finds
        // nothing left and lets go.
        _ = SecondWorkerAsync(second, details);
        await OwnWorkerAsync();
        var lines = _all.Select(p => p.Name + " " + (p.Done.Task.Status switch
        {
            TaskStatus.RanToCompletion when p.Done.Task.Result is null => "skipped",
            TaskStatus.RanToCompletion => "by " + p.PulledBy,
            TaskStatus.Canceled => "not started (stopped)",
            _ => "failed"
        }));
        SecondSignIn.Log($"{_label}: {string.Join(", ", lines)} · all done in {clock.ElapsedMilliseconds} ms");
    }

    private async Task OwnWorkerAsync()
    {
        while (!AllDone)
        {
            if (TryTake(out var pull))
            {
                pull.StartedAt = DateTime.Now;
                pull.PulledBy = "the app's own sign-in";
                try
                {
                    Complete(pull, await pull.OnOwn());
                }
                catch (Exception ex)
                {
                    pull.Done.TrySetException(ex);
                    Signal();
                }
                continue;
            }

            // Nothing left to start: wait for the second sign-in to finish or give one back.
            var changed = _changed.Task;
            if (AllDone) break;
            await changed;
        }
    }

    private async Task SecondWorkerAsync(SecondSignIn second, SecondSignIn.Details? details)
    {
        if (details is null || second.Disabled) return;
        try
        {
            await SecondWorkerCoreAsync(second, details);
        }
        catch (Exception ex)
        {
            SecondSignIn.Log("stopped by an error: " + ex.Message);
        }
    }

    private async Task SecondWorkerCoreAsync(SecondSignIn second, SecondSignIn.Details details)
    {
        var lease = await second.AcquireAsync(details, () => !Stopping && _waiting.Count > 0);
        if (lease is null) return;
        using (lease)
        {
            while (TryTake(out var pull))
            {
                pull.StartedAt = DateTime.Now;
                pull.PulledBy = "the second sign-in";
                try
                {
                    Complete(pull, await lease.FetchAsync(pull.OnSecond));
                }
                catch (Exception ex)
                {
                    lease.Failed(pull.Name, ex);
                    PutBack(pull);
                    break;
                }
            }
        }
    }
}
