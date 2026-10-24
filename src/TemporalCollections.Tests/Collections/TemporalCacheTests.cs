// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;

namespace TemporalCollections.Tests.Collections
{
    public class TemporalCacheTests
    {
        private sealed class ManualTimeProvider : TimeProvider
        {
            private DateTimeOffset _now;
            public ManualTimeProvider(DateTimeOffset start) => _now = start;
            public override DateTimeOffset GetUtcNow() => _now;
            public void Advance(TimeSpan delta) => _now += delta;
        }

        private static (TemporalCache<string, int> cache, ManualTimeProvider clock) Create(
            TimeSpan? ttl = null, bool sliding = false)
        {
            var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            var cache = new TemporalCache<string, int>(ttl ?? TimeSpan.FromMinutes(1), sliding, clock);
            return (cache, clock);
        }

        [Fact]
        public void Constructor_ThrowsOnNonPositiveTtl()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemporalCache<string, int>(TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemporalCache<string, int>(TimeSpan.FromSeconds(-1)));
        }

        [Fact]
        public void Constructor_SetsProperties()
        {
            var cache = new TemporalCache<string, int>(TimeSpan.FromSeconds(5), slidingExpiration: true);
            Assert.Equal(TimeSpan.FromSeconds(5), cache.DefaultTtl);
            Assert.True(cache.SlidingExpiration);
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void Constructor_UsesCustomComparer()
        {
            var cache = new TemporalCache<string, int>(TimeSpan.FromMinutes(1), comparer: StringComparer.OrdinalIgnoreCase);
            cache.Set("Key", 1);
            Assert.True(cache.TryGetValue("KEY", out var v));
            Assert.Equal(1, v);
        }

        [Fact]
        public void Set_And_TryGetValue_ReturnsValue()
        {
            var (cache, _) = Create();
            cache.Set("a", 1);
            Assert.True(cache.TryGetValue("a", out var v));
            Assert.Equal(1, v);
        }

        [Fact]
        public void TryGetValue_MissingKey_ReturnsFalse()
        {
            var (cache, _) = Create();
            Assert.False(cache.TryGetValue("missing", out var v));
            Assert.Equal(default, v);
        }

        [Fact]
        public void Set_OverwritesExistingValue()
        {
            var (cache, _) = Create();
            cache.Set("a", 1);
            cache.Set("a", 2);
            Assert.True(cache.TryGetValue("a", out var v));
            Assert.Equal(2, v);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void Set_ThrowsOnNonPositiveTtl()
        {
            var (cache, _) = Create();
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.Set("a", 1, TimeSpan.Zero));
        }

        [Fact]
        public void Entry_ExpiresAfterDefaultTtl()
        {
            var (cache, clock) = Create(TimeSpan.FromSeconds(10));
            cache.Set("a", 1);

            clock.Advance(TimeSpan.FromSeconds(9));
            Assert.True(cache.ContainsKey("a"));

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.False(cache.ContainsKey("a"));
            Assert.False(cache.TryGetValue("a", out _));
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void Entry_RespectsPerEntryTtl()
        {
            var (cache, clock) = Create(TimeSpan.FromSeconds(10));
            cache.Set("short", 1, TimeSpan.FromSeconds(2));
            cache.Set("long", 2);

            clock.Advance(TimeSpan.FromSeconds(3));
            Assert.False(cache.ContainsKey("short"));
            Assert.True(cache.ContainsKey("long"));
        }

        [Fact]
        public void AbsoluteExpiration_ReadDoesNotExtendLifetime()
        {
            var (cache, clock) = Create(TimeSpan.FromSeconds(10));
            cache.Set("a", 1);

            clock.Advance(TimeSpan.FromSeconds(8));
            Assert.True(cache.TryGetValue("a", out _));

            clock.Advance(TimeSpan.FromSeconds(3));
            Assert.False(cache.TryGetValue("a", out _));
        }

        [Fact]
        public void SlidingExpiration_ReadExtendsLifetime()
        {
            var (cache, clock) = Create(TimeSpan.FromSeconds(10), sliding: true);
            cache.Set("a", 1);

            clock.Advance(TimeSpan.FromSeconds(8));
            Assert.True(cache.TryGetValue("a", out _));

            clock.Advance(TimeSpan.FromSeconds(8));
            Assert.True(cache.TryGetValue("a", out _));

            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.False(cache.TryGetValue("a", out _));
        }

        [Fact]
        public void SlidingExpiration_ContainsKeyDoesNotExtendLifetime()
        {
            var (cache, clock) = Create(TimeSpan.FromSeconds(10), sliding: true);
            cache.Set("a", 1);

            clock.Advance(TimeSpan.FromSeconds(8));
            Assert.True(cache.ContainsKey("a"));

            clock.Advance(TimeSpan.FromSeconds(3));
            Assert.False(cache.ContainsKey("a"));
        }

        [Fact]
        public void TryAdd_AddsOnlyWhenAbsent()
        {
            var (cache, _) = Create();
            Assert.True(cache.TryAdd("a", 1));
            Assert.False(cache.TryAdd("a", 2));
            Assert.True(cache.TryGetValue("a", out var v));
            Assert.Equal(1, v);
        }

        [Fact]
        public void TryAdd_ReplacesExpiredEntry()
        {
            var (cache, clock) = Create(TimeSpan.FromSeconds(5));
            cache.TryAdd("a", 1);
            clock.Advance(TimeSpan.FromSeconds(6));

            Assert.True(cache.TryAdd("a", 2));
            Assert.True(cache.TryGetValue("a", out var v));
            Assert.Equal(2, v);
        }

        [Fact]
        public void TryAdd_ThrowsOnNonPositiveTtl()
        {
            var (cache, _) = Create();
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.TryAdd("a", 1, TimeSpan.FromSeconds(-1)));
        }

        [Fact]
        public void GetOrAdd_InvokesFactoryOnlyWhenMissing()
        {
            var (cache, _) = Create();
            int calls = 0;

            var v1 = cache.GetOrAdd("a", _ => { calls++; return 42; });
            var v2 = cache.GetOrAdd("a", _ => { calls++; return 99; });

            Assert.Equal(42, v1);
            Assert.Equal(42, v2);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void GetOrAdd_RecomputesAfterExpiration()
        {
            var (cache, clock) = Create(TimeSpan.FromSeconds(5));
            cache.GetOrAdd("a", _ => 1);
            clock.Advance(TimeSpan.FromSeconds(5));

            var v = cache.GetOrAdd("a", _ => 2);
            Assert.Equal(2, v);
        }

        [Fact]
        public void GetOrAdd_ThrowsOnNullFactory()
        {
            var (cache, _) = Create();
            Assert.Throws<ArgumentNullException>(() => cache.GetOrAdd("a", null!));
        }

        [Fact]
        public void TryGetItem_ReturnsTemporalItemWithKeyAndValue()
        {
            var (cache, _) = Create();
            cache.Set("a", 7);

            Assert.True(cache.TryGetItem("a", out var item));
            Assert.NotNull(item);
            Assert.Equal("a", item!.Value.Key);
            Assert.Equal(7, item.Value.Value);

            Assert.False(cache.TryGetItem("b", out var missing));
            Assert.Null(missing);
        }

        [Fact]
        public void TryRemove_RemovesLiveEntry()
        {
            var (cache, _) = Create();
            cache.Set("a", 1);

            Assert.True(cache.TryRemove("a", out var v));
            Assert.Equal(1, v);
            Assert.False(cache.ContainsKey("a"));
            Assert.False(cache.TryRemove("a", out _));
        }

        [Fact]
        public void TryRemove_ExpiredEntry_ReturnsFalse()
        {
            var (cache, clock) = Create(TimeSpan.FromSeconds(1));
            cache.Set("a", 1);
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.False(cache.TryRemove("a", out _));
        }

        [Fact]
        public void GetTimeToLive_ReturnsRemainingTime()
        {
            var (cache, clock) = Create(TimeSpan.FromSeconds(10));
            cache.Set("a", 1);
            clock.Advance(TimeSpan.FromSeconds(4));

            Assert.Equal(TimeSpan.FromSeconds(6), cache.GetTimeToLive("a"));
            Assert.Null(cache.GetTimeToLive("missing"));

            clock.Advance(TimeSpan.FromSeconds(6));
            Assert.Null(cache.GetTimeToLive("a"));
        }

        [Fact]
        public void Touch_ResetsLifetime()
        {
            var (cache, clock) = Create(TimeSpan.FromSeconds(10));
            cache.Set("a", 1);
            clock.Advance(TimeSpan.FromSeconds(8));

            Assert.True(cache.Touch("a"));
            Assert.Equal(TimeSpan.FromSeconds(10), cache.GetTimeToLive("a"));

            clock.Advance(TimeSpan.FromSeconds(11));
            Assert.False(cache.Touch("a"));
            Assert.False(cache.Touch("missing"));
        }

        [Fact]
        public void RemoveExpired_RemovesOnlyExpiredEntries()
        {
            var (cache, clock) = Create(TimeSpan.FromSeconds(10));
            cache.Set("a", 1, TimeSpan.FromSeconds(2));
            cache.Set("b", 2, TimeSpan.FromSeconds(3));
            cache.Set("c", 3);

            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(2, cache.RemoveExpired());
            Assert.Equal(1, cache.Count);
            Assert.Equal(new[] { "c" }, cache.Keys);
            Assert.Equal(0, cache.RemoveExpired());
        }

        [Fact]
        public void Keys_And_Count_ExcludeExpired()
        {
            var (cache, clock) = Create(TimeSpan.FromSeconds(10));
            cache.Set("a", 1, TimeSpan.FromSeconds(1));
            cache.Set("b", 2);

            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(1, cache.Count);
            Assert.Single(cache.Keys);
            Assert.Contains("b", cache.Keys);
        }

        [Fact]
        public void Clear_RemovesAll()
        {
            var (cache, _) = Create();
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Clear();
            Assert.Equal(0, cache.Count);
            Assert.Null(cache.GetLatest());
        }

        [Fact]
        public void GetInRange_ReturnsItemsOrderedByTimestamp()
        {
            var (cache, _) = Create();
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3);

            var all = cache.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToList();
            Assert.Equal(new[] { "a", "b", "c" }, all.Select(i => i.Value.Key));

            var bTs = all[1].Timestamp;
            var only = cache.GetInRange(bTs, bTs).ToList();
            Assert.Single(only);
            Assert.Equal("b", only[0].Value.Key);
        }

        [Fact]
        public void GetInRange_SwapsReversedBounds()
        {
            var (cache, _) = Create();
            cache.Set("a", 1);
            var ts = cache.GetLatest()!.Timestamp;

            var items = cache.GetInRange(ts.AddSeconds(1), ts.AddSeconds(-1)).ToList();
            Assert.Single(items);
        }

        [Fact]
        public void GetInRange_DateTimeOverload_Works()
        {
            var (cache, _) = Create();
            cache.Set("a", 1);
            var ts = cache.GetLatest()!.Timestamp.UtcDateTime;

            Assert.Single(cache.GetInRange(ts.AddSeconds(-1), ts.AddSeconds(1)));
        }

        [Fact]
        public void TimeQueries_ExcludeExpiredEntries()
        {
            var (cache, clock) = Create(TimeSpan.FromSeconds(10));
            cache.Set("a", 1, TimeSpan.FromSeconds(1));
            cache.Set("b", 2);
            clock.Advance(TimeSpan.FromSeconds(2));

            var all = cache.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToList();
            Assert.Single(all);
            Assert.Equal("b", all[0].Value.Key);
            Assert.Equal(1, cache.CountInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
            Assert.Equal("b", cache.GetEarliest()!.Value.Key);
        }

        [Fact]
        public void CountInRange_And_CountSince()
        {
            var (cache, _) = Create();
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3);

            var items = cache.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToList();
            Assert.Equal(2, cache.CountInRange(items[0].Timestamp, items[1].Timestamp));
            Assert.Equal(2, cache.CountSince(items[1].Timestamp));
            Assert.Equal(0, cache.CountSince(items[2].Timestamp.AddTicks(1)));
        }

        [Fact]
        public void GetBefore_And_GetAfter_AreExclusive()
        {
            var (cache, _) = Create();
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3);

            var mid = cache.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ElementAt(1).Timestamp;

            Assert.Equal(new[] { "a" }, cache.GetBefore(mid).Select(i => i.Value.Key));
            Assert.Equal(new[] { "c" }, cache.GetAfter(mid).Select(i => i.Value.Key));
        }

        [Fact]
        public void RemoveOlderThan_RemovesStrictlyOlder()
        {
            var (cache, _) = Create();
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3);

            var mid = cache.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ElementAt(1).Timestamp;
            cache.RemoveOlderThan(mid);

            Assert.False(cache.ContainsKey("a"));
            Assert.True(cache.ContainsKey("b"));
            Assert.True(cache.ContainsKey("c"));
        }

        [Fact]
        public void RemoveRange_RemovesInclusiveRange()
        {
            var (cache, _) = Create();
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3);

            var items = cache.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToList();
            cache.RemoveRange(items[1].Timestamp, items[2].Timestamp);

            Assert.Equal(new[] { "a" }, cache.Keys);
        }

        [Fact]
        public void GetNearest_ReturnsClosestItem()
        {
            var (cache, _) = Create();
            Assert.Null(cache.GetNearest(DateTimeOffset.UtcNow));

            cache.Set("a", 1);
            cache.Set("b", 2);
            var items = cache.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToList();

            Assert.Equal("a", cache.GetNearest(items[0].Timestamp.AddDays(-1))!.Value.Key);
            Assert.Equal("b", cache.GetNearest(items[1].Timestamp.AddDays(1))!.Value.Key);
        }

        [Fact]
        public void GetEarliest_GetLatest_GetTimeSpan()
        {
            var (cache, _) = Create();
            Assert.Null(cache.GetEarliest());
            Assert.Null(cache.GetLatest());
            Assert.Equal(TimeSpan.Zero, cache.GetTimeSpan());

            cache.Set("a", 1);
            Assert.Equal(TimeSpan.Zero, cache.GetTimeSpan());

            cache.Set("b", 2);
            var earliest = cache.GetEarliest()!;
            var latest = cache.GetLatest()!;
            Assert.Equal("a", earliest.Value.Key);
            Assert.Equal("b", latest.Value.Key);
            Assert.Equal(latest.Timestamp - earliest.Timestamp, cache.GetTimeSpan());
            Assert.True(cache.GetTimeSpan() > TimeSpan.Zero);
        }

        [Fact]
        public void Set_RefreshesWriteTimestamp()
        {
            var (cache, _) = Create();
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("a", 3);

            Assert.Equal("a", cache.GetLatest()!.Value.Key);
            Assert.Equal(3, cache.GetLatest()!.Value.Value);
        }

        [Fact]
        public void LargeTtl_DoesNotOverflow()
        {
            var (cache, _) = Create(TimeSpan.MaxValue);
            cache.Set("a", 1);
            Assert.True(cache.TryGetValue("a", out _));
            Assert.NotNull(cache.GetTimeToLive("a"));
        }

        [Fact]
        public void ConcurrentOperations_AreThreadSafe()
        {
            var cache = new TemporalCache<int, int>(TimeSpan.FromMinutes(1), slidingExpiration: true);

            Parallel.For(0, 10_000, i =>
            {
                int key = i % 100;
                cache.Set(key, i);
                cache.TryGetValue(key, out _);
                cache.GetOrAdd(key + 100, k => k);
                if (i % 7 == 0) cache.TryRemove(key, out _);
                if (i % 50 == 0) cache.RemoveExpired();
            });

            Assert.InRange(cache.Count, 100, 200);
            for (int k = 100; k < 200; k++)
                Assert.True(cache.ContainsKey(k));
        }

        [Fact]
        public void ConcurrentGetOrAdd_StoresSingleValue()
        {
            var cache = new TemporalCache<string, Guid>(TimeSpan.FromMinutes(1));
            var results = new System.Collections.Concurrent.ConcurrentBag<Guid>();

            Parallel.For(0, 1_000, _ => results.Add(cache.GetOrAdd("k", _ => Guid.NewGuid())));

            Assert.Single(results.Distinct());
        }
    }
}
