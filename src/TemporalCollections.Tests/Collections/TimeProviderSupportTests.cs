// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;
using TemporalCollections.Models;

namespace TemporalCollections.Tests.Collections
{
    public class TimeProviderSupportTests
    {
        private sealed class ManualTimeProvider : TimeProvider
        {
            private DateTimeOffset _now;
            public ManualTimeProvider(DateTimeOffset start) => _now = start;
            public override DateTimeOffset GetUtcNow() => _now;
            public void Advance(TimeSpan delta) => _now += delta;
        }

        private static readonly DateTimeOffset Start = new(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);

        // ---------- TemporalItem ----------

        [Fact]
        public void TemporalItem_Create_UsesProvidedClock()
        {
            var clock = new ManualTimeProvider(Start);
            var item = TemporalItem<string>.Create("a", clock);
            Assert.Equal(Start, item.Timestamp);
            Assert.Equal(TimeSpan.Zero, item.Timestamp.Offset);
        }

        [Fact]
        public void TemporalItem_Create_IsStrictlyMonotonicWithFrozenClock()
        {
            var clock = new ManualTimeProvider(Start);
            var a = TemporalItem<int>.Create(1, clock);
            var b = TemporalItem<int>.Create(2, clock);
            var c = TemporalItem<int>.Create(3, clock);

            Assert.Equal(Start.UtcTicks, a.Timestamp.UtcTicks);
            Assert.Equal(Start.UtcTicks + 1, b.Timestamp.UtcTicks);
            Assert.Equal(Start.UtcTicks + 2, c.Timestamp.UtcTicks);
        }

        [Fact]
        public void TemporalItem_Create_FollowsClockAdvance()
        {
            var clock = new ManualTimeProvider(Start);
            TemporalItem<int>.Create(1, clock);
            clock.Advance(TimeSpan.FromHours(1));
            var later = TemporalItem<int>.Create(2, clock);
            Assert.Equal(Start.AddHours(1), later.Timestamp);
        }

        [Fact]
        public void TemporalItem_Create_ClockGoingBackwards_StaysMonotonic()
        {
            var clock = new ManualTimeProvider(Start);
            var a = TemporalItem<int>.Create(1, clock);
            clock.Advance(TimeSpan.FromMinutes(-5));
            var b = TemporalItem<int>.Create(2, clock);
            Assert.True(b.Timestamp > a.Timestamp);
        }

        [Fact]
        public void TemporalItem_Create_PastClock_IsNotAffectedBySystemClock()
        {
            // System-clock items must not push a custom (past) clock forward.
            TemporalItem<long>.Create(0);
            var clock = new ManualTimeProvider(Start);
            var item = TemporalItem<long>.Create(1, clock);
            Assert.Equal(Start, item.Timestamp);
        }

        [Fact]
        public void TemporalItem_Create_IndependentClocks_DoNotInterfere()
        {
            var c1 = new ManualTimeProvider(Start);
            var c2 = new ManualTimeProvider(Start);
            var a = TemporalItem<short>.Create(1, c1);
            var b = TemporalItem<short>.Create(2, c2);
            Assert.Equal(Start, a.Timestamp);
            Assert.Equal(Start, b.Timestamp);
        }

        [Fact]
        public void TemporalItem_Create_SystemProvider_EquivalentToDefault()
        {
            var before = DateTimeOffset.UtcNow;
            var a = TemporalItem<byte>.Create(1, TimeProvider.System);
            var b = TemporalItem<byte>.Create(2);
            Assert.True(a.Timestamp >= before.AddSeconds(-1));
            Assert.True(b.Timestamp > a.Timestamp);
        }

        [Fact]
        public void TemporalItem_Create_NullProvider_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => TemporalItem<int>.Create(1, null!));
        }

        [Fact]
        public void TemporalItem_Create_ConcurrentWithFrozenClock_ProducesUniqueTimestamps()
        {
            var clock = new ManualTimeProvider(Start);
            var bag = new System.Collections.Concurrent.ConcurrentBag<long>();
            Parallel.For(0, 5_000, i => bag.Add(TemporalItem<Guid>.Create(Guid.Empty, clock).Timestamp.UtcTicks));
            Assert.Equal(5_000, bag.Distinct().Count());
        }

        // ---------- Backward compatibility ----------

        [Fact]
        public void DefaultConstructors_StillUseSystemClock()
        {
            var before = DateTimeOffset.UtcNow.AddSeconds(-1);

            var q = new TemporalQueue<int>(); q.Enqueue(1);
            var s = new TemporalStack<int>(); s.Push(1);
            var set = new TemporalSet<int>(); set.Add(1);
            var sl = new TemporalSortedList<int>(); sl.Add(1);
            var cb = new TemporalCircularBuffer<int>(2); cb.Add(1);
            var sa = new TemporalSegmentedArray<int>(); sa.AddValue(1);
            var pq = new TemporalPriorityQueue<int, int>(); pq.Enqueue(1, 1);
            var d = new TemporalDictionary<int, int>(); d.Add(1, 1);
            var mm = new TemporalMultimap<int, int>(); mm.AddValue(1, 1);
            var sw = new TemporalSlidingWindowSet<int>(TimeSpan.FromMinutes(1)); sw.Add(1);

            Assert.True(q.GetLatest()!.Timestamp >= before);
            Assert.True(s.GetLatest()!.Timestamp >= before);
            Assert.True(set.GetLatest()!.Timestamp >= before);
            Assert.True(sl.GetLatest()!.Timestamp >= before);
            Assert.True(cb.GetLatest()!.Timestamp >= before);
            Assert.True(sa.GetLatest()!.Timestamp >= before);
            Assert.True(pq.GetLatest()!.Timestamp >= before);
            Assert.True(d.GetLatest()!.Timestamp >= before);
            Assert.True(mm.GetLatest()!.Timestamp >= before);
            Assert.True(sw.GetLatest()!.Timestamp >= before);
        }

        [Fact]
        public void TemporalSet_ExistingComparerConstructor_StillWorks()
        {
            var set = new TemporalSet<string>(StringComparer.OrdinalIgnoreCase);
            Assert.True(set.Add("A"));
            Assert.False(set.Add("a"));
        }

        [Fact]
        public void Constructors_NullProvider_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new TemporalQueue<int>(null!));
            Assert.Throws<ArgumentNullException>(() => new TemporalStack<int>(null!));
            Assert.Throws<ArgumentNullException>(() => new TemporalSet<int>((TimeProvider)null!));
            Assert.Throws<ArgumentNullException>(() => new TemporalSortedList<int>(null!));
            Assert.Throws<ArgumentNullException>(() => new TemporalCircularBuffer<int>(2, null!));
            Assert.Throws<ArgumentNullException>(() => new TemporalSegmentedArray<int>((TimeProvider)null!));
            Assert.Throws<ArgumentNullException>(() => new TemporalPriorityQueue<int, int>(null!));
            Assert.Throws<ArgumentNullException>(() => new TemporalDictionary<int, int>(null!));
            Assert.Throws<ArgumentNullException>(() => new TemporalMultimap<int, int>(null!));
            Assert.Throws<ArgumentNullException>(() => new TemporalSlidingWindowSet<int>(TimeSpan.FromMinutes(1), null!));
        }

        // ---------- Clock isolation ----------

        [Fact]
        public void TemporalItem_Create_FutureClock_DoesNotAdvanceSystemClock()
        {
            var future = new ManualTimeProvider(DateTimeOffset.UtcNow.AddYears(10));
            var f = TemporalItem<decimal>.Create(1m, future);

            var sys = TemporalItem<decimal>.Create(2m);

            Assert.True(sys.Timestamp < f.Timestamp);
            Assert.True(sys.Timestamp < DateTimeOffset.UtcNow.AddMinutes(1));
        }

        [Fact]
        public void Collection_FutureClock_DoesNotAffectSystemClockCollection()
        {
            var future = new ManualTimeProvider(DateTimeOffset.UtcNow.AddYears(10));
            var fq = new TemporalQueue<sbyte>(future);
            var sq = new TemporalQueue<sbyte>();

            fq.Enqueue(1);
            sq.Enqueue(2);

            Assert.True(sq.GetLatest()!.Timestamp < DateTimeOffset.UtcNow.AddMinutes(1));
            Assert.True(fq.GetLatest()!.Timestamp > DateTimeOffset.UtcNow.AddYears(9));
        }

        [Fact]
        public void Collections_SharingSameClock_ProduceStrictlyIncreasingTimestamps()
        {
            var clock = new ManualTimeProvider(Start);
            var q1 = new TemporalQueue<ushort>(clock);
            var q2 = new TemporalQueue<ushort>(clock);

            q1.Enqueue(1);
            q2.Enqueue(2);
            q1.Enqueue(3);

            var t1 = q1.GetEarliest()!.Timestamp;
            var t2 = q2.GetEarliest()!.Timestamp;
            var t3 = q1.GetLatest()!.Timestamp;

            Assert.True(t1 < t2);
            Assert.True(t2 < t3);
            Assert.Equal(Start, t1);
        }

        // ---------- Constructor argument validation (new overloads) ----------

        [Fact]
        public void TemporalCircularBuffer_TimeProviderCtor_InvalidCapacity_Throws()
        {
            var clock = new ManualTimeProvider(Start);
            Assert.Throws<ArgumentException>(() => new TemporalCircularBuffer<int>(0, clock));
            Assert.Throws<ArgumentException>(() => new TemporalCircularBuffer<int>(-1, clock));
        }

        [Fact]
        public void TemporalSegmentedArray_TimeProviderCtor_InvalidCapacity_Throws()
        {
            var clock = new ManualTimeProvider(Start);
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemporalSegmentedArray<int>(clock, segmentCapacity: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemporalSegmentedArray<int>(clock, segmentCapacity: 0));
        }

        [Fact]
        public void TemporalSlidingWindowSet_TimeProviderCtor_InvalidWindow_Throws()
        {
            var clock = new ManualTimeProvider(Start);
            Assert.Throws<ArgumentException>(() => new TemporalSlidingWindowSet<int>(TimeSpan.Zero, clock));
            Assert.Throws<ArgumentException>(() => new TemporalSlidingWindowSet<int>(TimeSpan.FromSeconds(-1), clock));
        }

        [Fact]
        public void TemporalCache_InvalidTtlWithClock_Throws()
        {
            var clock = new ManualTimeProvider(Start);
            Assert.Throws<ArgumentOutOfRangeException>(() => new TemporalCache<int, int>(TimeSpan.Zero, timeProvider: clock));
        }

        [Fact]
        public void TemporalCache_NullClock_FallsBackToSystem()
        {
            var cache = new TemporalCache<int, int>(TimeSpan.FromMinutes(1), timeProvider: null);
            cache.Set(1, 1);
            Assert.True(cache.GetLatest()!.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        // ---------- Concurrency with frozen clock ----------

        [Fact]
        public void TemporalStack_ConcurrentPush_FrozenClock_KeepsOrderedUniqueTimestamps()
        {
            var clock = new ManualTimeProvider(Start);
            var s = new TemporalStack<int>(clock);

            Parallel.For(0, 2_000, i => s.Push(i));

            var items = s.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)
                .Select(i => i.Timestamp.UtcTicks).ToList();
            Assert.Equal(2_000, s.Count);
            Assert.Equal(2_000, items.Distinct().Count());
            Assert.Equal(Start, s.GetEarliest()!.Timestamp);
            Assert.Equal(Start.AddTicks(1_999), s.GetLatest()!.Timestamp);
        }

        [Fact]
        public void TemporalSortedList_ConcurrentAdd_FrozenClock_KeepsSortedUniqueTimestamps()
        {
            var clock = new ManualTimeProvider(Start);
            var sl = new TemporalSortedList<int>(clock);

            Parallel.For(0, 2_000, i => sl.Add(i));

            var ticks = sl.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)
                .Select(i => i.Timestamp.UtcTicks).ToList();
            Assert.Equal(2_000, ticks.Count);
            Assert.Equal(ticks.OrderBy(t => t), ticks);
            Assert.Equal(2_000, ticks.Distinct().Count());
        }

        [Fact]
        public void TemporalDictionary_ConcurrentAdd_FrozenClock_UniqueTimestamps()
        {
            var clock = new ManualTimeProvider(Start);
            var d = new TemporalDictionary<int, int>(clock);

            Parallel.For(0, 2_000, i => d.Add(i % 10, i));

            var ticks = d.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)
                .Select(i => i.Timestamp.UtcTicks).ToList();
            Assert.Equal(2_000, ticks.Count);
            Assert.Equal(2_000, ticks.Distinct().Count());
        }

        // ---------- Collections use the injected clock ----------

        [Fact]
        public void TemporalQueue_UsesInjectedClock()
        {
            var clock = new ManualTimeProvider(Start);
            var q = new TemporalQueue<string>(clock);
            q.Enqueue("a");
            clock.Advance(TimeSpan.FromMinutes(10));
            q.Enqueue("b");

            Assert.Equal(Start, q.GetEarliest()!.Timestamp);
            Assert.Equal(Start.AddMinutes(10), q.GetLatest()!.Timestamp);
            Assert.Equal(TimeSpan.FromMinutes(10), q.GetTimeSpan());

            q.RemoveOlderThan(Start.AddMinutes(5));
            Assert.Equal(1, q.Count);
        }

        [Fact]
        public void TemporalStack_UsesInjectedClock()
        {
            var clock = new ManualTimeProvider(Start);
            var s = new TemporalStack<string>(clock);
            s.Push("a");
            clock.Advance(TimeSpan.FromMinutes(1));
            s.Push("b");

            Assert.Single(s.GetInRange(Start, Start));
            Assert.Equal(Start.AddMinutes(1), s.GetLatest()!.Timestamp);
        }

        [Fact]
        public void TemporalSet_UsesInjectedClock()
        {
            var clock = new ManualTimeProvider(Start);
            var set = new TemporalSet<string>(clock, StringComparer.OrdinalIgnoreCase);
            set.Add("a");
            clock.Advance(TimeSpan.FromDays(1));
            set.Add("b");

            Assert.Equal(1, set.CountInRange(Start, Start.AddHours(1)));
            Assert.False(set.Add("A"));
        }

        [Fact]
        public void TemporalSortedList_UsesInjectedClock()
        {
            var clock = new ManualTimeProvider(Start);
            var sl = new TemporalSortedList<int>(clock);
            for (int i = 0; i < 5; i++)
            {
                sl.Add(i);
                clock.Advance(TimeSpan.FromSeconds(1));
            }

            Assert.Equal(3, sl.CountInRange(Start.AddSeconds(1), Start.AddSeconds(3)));
            Assert.Equal(Start.AddSeconds(2), sl.GetNearest(Start.AddSeconds(2.2))!.Timestamp);
        }

        [Fact]
        public void TemporalCircularBuffer_UsesInjectedClock()
        {
            var clock = new ManualTimeProvider(Start);
            var cb = new TemporalCircularBuffer<int>(3, clock);
            cb.Add(1);
            clock.Advance(TimeSpan.FromMinutes(1));
            cb.Add(2);

            Assert.Equal(Start, cb.GetEarliest()!.Timestamp);
            Assert.Single(cb.GetAfter(Start));
        }

        [Fact]
        public void TemporalSegmentedArray_UsesInjectedClock()
        {
            var clock = new ManualTimeProvider(Start);
            var sa = new TemporalSegmentedArray<int>(clock, segmentCapacity: 2);
            for (int i = 0; i < 6; i++)
            {
                sa.AddValue(i);
                clock.Advance(TimeSpan.FromMinutes(1));
            }

            sa.RemoveOlderThan(Start.AddMinutes(3));
            Assert.Equal(3, sa.Count);
            Assert.Equal(Start.AddMinutes(3), sa.GetEarliest()!.Timestamp);
        }

        [Fact]
        public void TemporalPriorityQueue_UsesInjectedClock()
        {
            var clock = new ManualTimeProvider(Start);
            var pq = new TemporalPriorityQueue<int, string>(clock);
            pq.Enqueue("a", 1);
            clock.Advance(TimeSpan.FromHours(1));
            pq.Enqueue("b", 2);

            Assert.Equal(1, pq.CountSince(Start.AddMinutes(30)));
            Assert.Equal(Start, pq.GetEarliest()!.Timestamp);
        }

        [Fact]
        public void TemporalDictionary_UsesInjectedClock()
        {
            var clock = new ManualTimeProvider(Start);
            var d = new TemporalDictionary<string, int>(clock);
            d.Add("k", 1);
            clock.Advance(TimeSpan.FromMinutes(2));
            d.Add("k", 2);

            var items = d.GetInRange("k", Start, Start.AddMinutes(1)).ToList();
            Assert.Single(items);
            Assert.Equal(1, items[0].Value);
        }

        [Fact]
        public void TemporalMultimap_UsesInjectedClock()
        {
            var clock = new ManualTimeProvider(Start);
            var mm = new TemporalMultimap<string, int>(clock);
            var first = mm.AddValue("k", 1);
            clock.Advance(TimeSpan.FromMinutes(2));
            var second = mm.AddValue("k", 2);

            Assert.Equal(Start, first.Timestamp);
            Assert.Equal(Start.AddMinutes(2), second.Timestamp);
        }

        [Fact]
        public void TemporalSlidingWindowSet_RemoveExpired_UsesInjectedClock()
        {
            var clock = new ManualTimeProvider(Start);
            var sw = new TemporalSlidingWindowSet<string>(TimeSpan.FromMinutes(5), clock);
            sw.Add("old");
            clock.Advance(TimeSpan.FromMinutes(4));
            sw.Add("new");

            clock.Advance(TimeSpan.FromMinutes(2)); // old = 6 min, new = 2 min
            sw.RemoveExpired();

            var values = sw.GetItems().Select(i => i.Value).ToList();
            Assert.Equal(new[] { "new" }, values);

            clock.Advance(TimeSpan.FromMinutes(10));
            sw.RemoveExpired();
            Assert.Empty(sw.GetItems());
        }

        [Fact]
        public void TemporalCache_WriteTimestampAndExpiration_ShareSameClock()
        {
            var clock = new ManualTimeProvider(Start);
            var cache = new TemporalCache<string, int>(TimeSpan.FromMinutes(5), timeProvider: clock);
            cache.Set("a", 1);
            clock.Advance(TimeSpan.FromMinutes(3));
            cache.Set("b", 2);

            Assert.Equal(Start, cache.GetEarliest()!.Timestamp);
            Assert.Equal(Start.AddMinutes(3), cache.GetLatest()!.Timestamp);

            clock.Advance(TimeSpan.FromMinutes(3)); // a expired, b alive
            var live = cache.GetInRange(Start, Start.AddHours(1)).Select(i => i.Value.Key).ToList();
            Assert.Equal(new[] { "b" }, live);
        }
    }
}
