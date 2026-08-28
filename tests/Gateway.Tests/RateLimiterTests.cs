using Gateway.Services;
using Xunit;

namespace Gateway.Tests;

public class RateLimiterTests
{
    [Fact]
    public void AllowsRequestsWithinBudget()
    {
        var limiter = new TokenBucketRateLimiter(rpm: 10, tpm: 1000);
        var result = limiter.TryConsume("client-a", estimatedTokens: 100);
        Assert.True(result.Allowed);
    }

    [Fact]
    public void BlocksWhenRequestBudgetExhausted()
    {
        var limiter = new TokenBucketRateLimiter(rpm: 2, tpm: 100_000);

        Assert.True(limiter.TryConsume("client-a", 10).Allowed);
        Assert.True(limiter.TryConsume("client-a", 10).Allowed);

        var third = limiter.TryConsume("client-a", 10);
        Assert.False(third.Allowed);
        Assert.True(third.RetryAfterSeconds > 0);
    }

    [Fact]
    public void BlocksWhenTokenBudgetExhaustedEvenWithRequestsLeft()
    {
        var limiter = new TokenBucketRateLimiter(rpm: 100, tpm: 50);

        Assert.True(limiter.TryConsume("client-a", 40).Allowed);
        Assert.False(limiter.TryConsume("client-a", 40).Allowed);
    }

    [Fact]
    public void DifferentApiKeysHaveIndependentBuckets()
    {
        var limiter = new TokenBucketRateLimiter(rpm: 1, tpm: 1000);

        Assert.True(limiter.TryConsume("client-a", 10).Allowed);
        Assert.False(limiter.TryConsume("client-a", 10).Allowed);
        Assert.True(limiter.TryConsume("client-b", 10).Allowed);
    }
}
