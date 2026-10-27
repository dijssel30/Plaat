using Microsoft.Extensions.Caching.Memory;
using Plaat.Db.Repositories;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class LibraryCacheTests
{
    private static LibraryCache NewCache()
    {
        return new(new MemoryCache(new MemoryCacheOptions()));
    }

    [Fact]
    public async Task GetOrCreateAsync_RunsFactoryOnce_UnderConcurrency()
    {
        var cache = NewCache();
        var calls = 0;

        async ValueTask<int> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(50);

            return 42;
        }

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ =>
                cache.GetOrCreateAsync("k", Factory, CancellationToken.None).AsTask()));

        calls.ShouldBe(1);
        results.ShouldAllBe(r => r == 42);
    }

    [Fact]
    public void Mutate_IsNoOp_WhenKeyNotCached()
    {
        var cache = NewCache();

        Should.NotThrow(() => cache.Mutate<List<int>>("missing", list => list.Add(1)));
        cache.TryGet<List<int>>("missing", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Mutate_EditsCachedValueInPlace()
    {
        var cache = NewCache();
        var list = await cache.GetOrCreateAsync("k", _ => ValueTask.FromResult(new List<int> { 1 }), CancellationToken.None);

        cache.Mutate<List<int>>("k", l => l.Add(2));

        list.ShouldBe([1, 2]);
        cache.TryGet<List<int>>("k", out var again).ShouldBeTrue();
        again.ShouldBeSameAs(list);
    }

    [Fact]
    public void Update_ReplacesValue_OnlyWhenPresent()
    {
        var cache = NewCache();

        cache.Update<int>("k", v => v + 1); // cold - no-op
        cache.TryGet<int>("k", out _).ShouldBeFalse();

        cache.Set("k", 10);
        cache.Update<int>("k", v => v + 5);

        cache.TryGet<int>("k", out var value).ShouldBeTrue();
        value.ShouldBe(15);
    }

    [Fact]
    public void RemoveByPrefix_ClearsOnlyMatchingKeys()
    {
        var cache = NewCache();
        cache.Set("tracks:details:1", 1);
        cache.Set("tracks:details:2", 2);
        cache.Set("tracks:all", 99);

        cache.RemoveByPrefix("tracks:details:");

        cache.TryGet<int>("tracks:details:1", out _).ShouldBeFalse();
        cache.TryGet<int>("tracks:details:2", out _).ShouldBeFalse();
        cache.TryGet<int>("tracks:all", out var all).ShouldBeTrue();
        all.ShouldBe(99);
    }

    [Fact]
    public async Task Gates_AreDropped_WhenIdle()
    {
        var cache = NewCache();

        for (var i = 0; i < 50; i++)
        {
            var key = $"tracks:details:{i}";
            await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult(i), CancellationToken.None);
            cache.Mutate<int>(key, _ => { });
            cache.Update<int>(key, v => v + 1);
            cache.Remove(key);
        }

        cache.Mutate<int>("never-cached", _ => { });

        cache.ActiveGateCount.ShouldBe(0);
    }

    [Fact]
    public async Task Gates_AreDropped_WhenWaitIsCancelled()
    {
        var cache = NewCache();
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var holder = cache.GetOrCreateAsync("k", async _ =>
        {
            started.SetResult();
            await release.Task;

            return 1;
        }, CancellationToken.None).AsTask();

        await started.Task;

        using var cts = new CancellationTokenSource(20);
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await cache.GetOrCreateAsync("k", _ => ValueTask.FromResult(2), cts.Token));

        cache.ActiveGateCount.ShouldBe(1);

        release.SetResult();
        await holder;

        cache.ActiveGateCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetOrCreateAsync_StaysSingleFlight_WhenRemovedWhileInFlight()
    {
        var cache = NewCache();
        var calls = 0;

        async ValueTask<int> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(50);

            return 1;
        }

        var first = cache.GetOrCreateAsync("k", Factory, CancellationToken.None).AsTask();
        await Task.Delay(10);
        cache.Remove("k");
        var second = cache.GetOrCreateAsync("k", Factory, CancellationToken.None).AsTask();

        await Task.WhenAll(first, second);

        calls.ShouldBe(1);
    }
}
