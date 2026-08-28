using Gateway.Services;
using Xunit;

namespace Gateway.Tests;

public class CircuitBreakerTests
{
    [Fact]
    public void OpensAfterThresholdConsecutiveFailures()
    {
        var breaker = new SimpleCircuitBreaker(failureThreshold: 3, breakDuration: TimeSpan.FromSeconds(30));

        Assert.False(breaker.IsOpen);
        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.False(breaker.IsOpen);
        breaker.RecordFailure();
        Assert.True(breaker.IsOpen);
    }

    [Fact]
    public void SuccessResetsFailureCount()
    {
        var breaker = new SimpleCircuitBreaker(failureThreshold: 2, breakDuration: TimeSpan.FromSeconds(30));

        breaker.RecordFailure();
        breaker.RecordSuccess();
        breaker.RecordFailure();

        Assert.False(breaker.IsOpen); // only 1 consecutive failure since the reset
    }

    [Fact]
    public void HalfOpensAfterBreakDuration()
    {
        var breaker = new SimpleCircuitBreaker(failureThreshold: 1, breakDuration: TimeSpan.FromMilliseconds(50));

        breaker.RecordFailure();
        Assert.True(breaker.IsOpen);

        Thread.Sleep(80);
        Assert.False(breaker.IsOpen); // half-open: one trial request let through
    }
}
