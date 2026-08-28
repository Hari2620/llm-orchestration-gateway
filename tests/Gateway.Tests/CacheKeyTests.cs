using Gateway.Models;
using Gateway.Services;
using Xunit;

namespace Gateway.Tests;

public class CacheKeyTests
{
    [Fact]
    public void SamePromptProducesSameKeyDifferentPromptDoesNot()
    {
        var keyA = InMemoryCacheStore.BuildKey("chat-support", "v1", "rendered prompt text", 512);
        var keyB = InMemoryCacheStore.BuildKey("chat-support", "v1", "rendered prompt text", 512);
        var keyC = InMemoryCacheStore.BuildKey("chat-support", "v1", "a different prompt", 512);

        Assert.Equal(keyA, keyB);
        Assert.NotEqual(keyA, keyC);
    }

    [Fact]
    public void DifferentMaxTokensProducesDifferentKey()
    {
        var keyA = InMemoryCacheStore.BuildKey("chat-support", "v1", "same text", 256);
        var keyB = InMemoryCacheStore.BuildKey("chat-support", "v1", "same text", 512);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void CacheStoreRespectsExpiry()
    {
        var store = new InMemoryCacheStore();
        var key = InMemoryCacheStore.BuildKey("p", "v1", "text", 100);
        var result = new LlmCompletionResult("hello", "mock", 1, 1);

        store.Set(key, result, TimeSpan.FromMilliseconds(50));
        Assert.True(store.TryGet(key, out var hit));
        Assert.Equal("hello", hit!.Text);

        Thread.Sleep(120);
        Assert.False(store.TryGet(key, out _));
    }
}
