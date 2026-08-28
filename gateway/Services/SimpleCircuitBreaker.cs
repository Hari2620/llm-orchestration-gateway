namespace Gateway.Services;

/// <summary>
/// Deliberately minimal circuit breaker: N consecutive failures opens it, one trial
/// request after the break window decides whether to close it again (half-open).
/// No exponential backoff, no per-error-type weighting — a real deployment fronting
/// a paid model API would reach for Polly's CircuitBreakerPolicy instead. This is
/// the ~15-line version that's enough to demonstrate (and unit-test) the pattern.
/// </summary>
public class SimpleCircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _breakDuration;
    private readonly object _lock = new();

    private int _consecutiveFailures;
    private DateTime _openedAt = DateTime.MinValue;

    public SimpleCircuitBreaker(int failureThreshold, TimeSpan breakDuration)
    {
        _failureThreshold = failureThreshold;
        _breakDuration = breakDuration;
    }

    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                if (_consecutiveFailures < _failureThreshold) return false;

                if (DateTime.UtcNow - _openedAt > _breakDuration)
                {
                    // Half-open: let exactly one request through as a trial.
                    _consecutiveFailures = _failureThreshold - 1;
                    return false;
                }

                return true;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock) _consecutiveFailures = 0;
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= _failureThreshold) _openedAt = DateTime.UtcNow;
        }
    }
}
