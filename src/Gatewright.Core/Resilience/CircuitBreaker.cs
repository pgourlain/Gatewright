// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

namespace Gatewright.Core.Resilience;

/// <summary>A thread-safe circuit breaker: closed → open after consecutive failures → half-open after the break (one trial call) → closed on success.</summary>
public sealed class CircuitBreaker
{
    private readonly object _gate = new();
    private readonly int _threshold;
    private readonly TimeSpan _break;
    private readonly TimeProvider _time;
    private int _failures;
    private DateTimeOffset? _openedAt;
    private bool _trialInFlight;

    /// <summary>Initializes a new instance of the <see cref="CircuitBreaker"/> class.</summary>
    /// <param name="threshold">Consecutive failures that open the circuit.</param>
    /// <param name="breakDuration">How long the circuit stays open before a trial call.</param>
    /// <param name="time">The clock.</param>
    public CircuitBreaker(int threshold, TimeSpan breakDuration, TimeProvider time)
    {
        _threshold = threshold;
        _break = breakDuration;
        _time = time;
    }

    /// <summary>Gets a value indicating whether the circuit is open or half-open.</summary>
    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _openedAt is not null;
            }
        }
    }

    /// <summary>Asks permission to make a call. When denied, <paramref name="retryAfter"/> tells how long the circuit stays open.</summary>
    /// <param name="retryAfter">How long until a trial call is allowed.</param>
    /// <returns><see langword="true"/> when the call may proceed.</returns>
    public bool TryAcquire(out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            retryAfter = TimeSpan.Zero;
            if (_openedAt is not { } opened)
            {
                return true;
            }

            var remaining = opened + _break - _time.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                retryAfter = remaining;
                return false;
            }

            if (_trialInFlight)
            {
                retryAfter = TimeSpan.FromSeconds(1);
                return false;
            }

            _trialInFlight = true;
            return true;
        }
    }

    /// <summary>Records a successful call: closes the circuit.</summary>
    public void RecordSuccess()
    {
        lock (_gate)
        {
            _failures = 0;
            _openedAt = null;
            _trialInFlight = false;
        }
    }

    /// <summary>Records a failed call.</summary>
    public void RecordFailure()
    {
        lock (_gate)
        {
            _failures++;
            if (_openedAt is not null || _failures >= _threshold)
            {
                _openedAt = _time.GetUtcNow();
                _trialInFlight = false;
            }
        }
    }
}
