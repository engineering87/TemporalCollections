// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;

namespace TemporalCollections.Tests.Collections
{
    public class TemporalSortedListTests
    {
        [Fact]
        public void Add_Items_AreSortedByTimestamp()
        {
            var list = new TemporalSortedList<string>();

            list.Add("first");
            Thread.Sleep(10);
            list.Add("second");
            Thread.Sleep(10);
            list.Add("third");

            var snapshot = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();

            Assert.Equal(3, snapshot.Count);

            for (int i = 1; i < snapshot.Count; i++)
            {
                Assert.True(snapshot[i].Timestamp >= snapshot[i - 1].Timestamp);
            }
        }

        [Fact]
        public void GetInRange_ReturnsCorrectItems()
        {
            var list = new TemporalSortedList<string>();

            list.Add("a");
            Thread.Sleep(10);
            list.Add("b");
            Thread.Sleep(10);
            list.Add("c");

            var allItems = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            var timeA = allItems.First(i => i.Value == "a").Timestamp; // DateTimeOffset
            var timeB = allItems.First(i => i.Value == "b").Timestamp; // DateTimeOffset
            var timeC = allItems.First(i => i.Value == "c").Timestamp; // DateTimeOffset

            var from = timeA.AddMilliseconds(1).UtcDateTime;
            var to = timeC.AddMilliseconds(1).UtcDateTime;

            var items = list.GetInRange(from, to).ToList();

            Assert.Contains(items, item => item.Value == "b");
            Assert.Contains(items, item => item.Value == "c");
            Assert.DoesNotContain(items, item => item.Value == "a");
        }

        [Fact]
        public void RemoveOlderThan_RemovesCorrectItems()
        {
            var list = new TemporalSortedList<string>();

            list.Add("a");
            Thread.Sleep(5);
            list.Add("b");
            Thread.Sleep(5);
            list.Add("c");

            var snap = list.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                           .OrderBy(i => i.Timestamp)
                           .ToList();

            Assert.Equal(3, snap.Count);

            var tA = snap[0].Timestamp; // DateTimeOffset
            var tB = snap[1].Timestamp; // DateTimeOffset

            // cutoff strictly between A and B (in UTC ticks)
            var cutoffTicks = (tA.UtcTicks + tB.UtcTicks) / 2;
            var cutoff = new DateTime(cutoffTicks, DateTimeKind.Utc);

            list.RemoveOlderThan(cutoff);

            var remaining = list.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                                .OrderBy(i => i.Timestamp)
                                .Select(i => i.Value)
                                .ToList();

            Assert.Equal(new[] { "b", "c" }, remaining);
        }

        [Fact]
        public void Count_ReflectsNumberOfItems()
        {
            var list = new TemporalSortedList<int>();
            Assert.Equal(0, list.Count);

            list.Add(1);
            Assert.Equal(1, list.Count);

            list.Add(2);
            Assert.Equal(2, list.Count);

            var cutoff = DateTime.UtcNow.AddSeconds(1);
            list.RemoveOlderThan(cutoff);

            Assert.True(list.Count <= 2);
        }

        [Fact]
        public void CountSince_ShouldBeInclusive_AndConsistentWithGetInRange()
        {
            var list = new TemporalSortedList<int>();

            list.Add(1);
            Thread.Sleep(5);
            list.Add(2);
            Thread.Sleep(5);
            list.Add(3);

            var all = list.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                          .OrderBy(i => i.Timestamp)
                          .ToList();

            Assert.True(all.Count >= 2, "Need at least two items for a valid cutoff.");

            // Choose timestamp of the 2nd item as cutoff
            var cutoff = all[1].Timestamp.UtcDateTime;

            // Expected count = items at index 1 and onward
            var expected = all.Count - 1;

            var countSince = list.CountSince(cutoff);

            Assert.Equal(expected, countSince);

            // Cross-check with GetInRange(cutoff, now)
            var cross = list.GetInRange(cutoff, DateTime.UtcNow).Count();
            Assert.Equal(cross, countSince);
        }

        [Fact]
        public void TemporalSortedList_GetNearest_WorksAndTiesPreferLater()
        {
            var lst = new TemporalSortedList<int>();
            lst.Add(1);
            lst.Add(2);
            lst.Add(3);

            var all = lst.GetInRange(
                new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            ).ToList();

            Assert.True(all.Count >= 3);

            var b = all[1]; // 2
            var c = all[2]; // 3

            // Exact hit on B
            var exact = lst.GetNearest(b.Timestamp.UtcDateTime);
            Assert.NotNull(exact);
            Assert.Equal(2, exact!.Value);

            long dt = c.Timestamp.UtcTicks - b.Timestamp.UtcTicks;
            Assert.True(dt > 0, "Timestamps should be strictly increasing");

            if ((dt & 1L) == 0L)
            {
                // Delta even: true midpoint -> tie-break must prefer later (C)
                long midTicks = b.Timestamp.UtcTicks + (dt / 2);
                var mid = new DateTimeOffset(midTicks, TimeSpan.Zero).UtcDateTime;

                var tie = lst.GetNearest(mid);
                Assert.NotNull(tie);
                Assert.Equal(3, tie!.Value);
            }
            else
            {
                // Delta odd: no true tie; test both sides around midpoint
                long midFloorTicks = b.Timestamp.UtcTicks + (dt / 2); // nearer to B
                long midCeilTicks = midFloorTicks + 1;               // nearer to C

                var midFloor = new DateTimeOffset(midFloorTicks, TimeSpan.Zero).UtcDateTime;
                var midCeil = new DateTimeOffset(midCeilTicks, TimeSpan.Zero).UtcDateTime;

                var nearFloor = lst.GetNearest(midFloor);
                var nearCeil = lst.GetNearest(midCeil);

                Assert.NotNull(nearFloor);
                Assert.NotNull(nearCeil);

                Assert.Equal(2, nearFloor!.Value);
                Assert.Equal(3, nearCeil!.Value);
            }
        }

        [Fact]
        public void GetInRange_WhenFromGreaterThanTo_SwapsAndReturnsSameResults()
        {
            var list = new TemporalSortedList<string>();
            list.Add("a");
            list.Add("b");
            list.Add("c");

            var all = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            Assert.Equal(3, all.Count);

            var min = all.First().Timestamp.UtcDateTime;
            var max = all.Last().Timestamp.UtcDateTime;

            var normal = list.GetInRange(min, max).Select(i => i.Value).ToList();
            var swapped = list.GetInRange(max, min).Select(i => i.Value).ToList();

            Assert.Equal(normal, swapped);
        }

        [Fact]
        public void GetInRange_OnEmptyList_ReturnsEmpty()
        {
            var list = new TemporalSortedList<int>();
            var items = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            Assert.Empty(items);
        }

        [Fact]
        public void CountInRange_OnEmptyList_ReturnsZero()
        {
            var list = new TemporalSortedList<int>();
            Assert.Equal(0, list.CountInRange(DateTime.MinValue, DateTime.MaxValue));
        }

        [Fact]
        public void GetLatest_And_GetEarliest_OnEmptyList_ReturnNull()
        {
            var list = new TemporalSortedList<int>();
            Assert.Null(list.GetLatest());
            Assert.Null(list.GetEarliest());
        }

        [Fact]
        public void GetLatest_And_GetEarliest_ReturnCorrectItems()
        {
            var list = new TemporalSortedList<string>();
            list.Add("first");
            list.Add("second");
            list.Add("third");

            var earliest = list.GetEarliest();
            var latest = list.GetLatest();

            Assert.NotNull(earliest);
            Assert.NotNull(latest);

            Assert.Equal("first", earliest!.Value);
            Assert.Equal("third", latest!.Value);

            Assert.True(earliest.Timestamp <= latest.Timestamp);
        }

        [Fact]
        public void GetBefore_IsStrictlyBefore_Cutoff()
        {
            var list = new TemporalSortedList<int>();
            list.Add(1);
            list.Add(2);
            list.Add(3);

            var all = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            var t2 = all.Single(i => i.Value == 2).Timestamp.UtcDateTime;

            var before = list.GetBefore(t2).Select(i => i.Value).ToList();

            Assert.Contains(1, before);
            Assert.DoesNotContain(2, before); // Strictly before
            Assert.DoesNotContain(3, before);
        }

        [Fact]
        public void GetAfter_IsStrictlyAfter_Cutoff()
        {
            var list = new TemporalSortedList<int>();
            list.Add(1);
            list.Add(2);
            list.Add(3);

            var all = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            var t2 = all.Single(i => i.Value == 2).Timestamp.UtcDateTime;

            var after = list.GetAfter(t2).Select(i => i.Value).ToList();

            Assert.Contains(3, after);
            Assert.DoesNotContain(2, after); // Strictly after
            Assert.DoesNotContain(1, after);
        }

        [Fact]
        public void RemoveRange_RemovesInclusiveBounds()
        {
            var list = new TemporalSortedList<int>();
            list.Add(1);
            list.Add(2);
            list.Add(3);
            list.Add(4);

            var all = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            var t2 = all.Single(i => i.Value == 2).Timestamp.UtcDateTime;
            var t3 = all.Single(i => i.Value == 3).Timestamp.UtcDateTime;

            // Remove [2,3] inclusive
            list.RemoveRange(t2, t3);

            var remaining = list.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                                .Select(i => i.Value)
                                .ToList();

            Assert.Equal(new[] { 1, 4 }, remaining);
        }

        [Fact]
        public void RemoveRange_WhenOutsideBounds_DoesNothing()
        {
            var list = new TemporalSortedList<int>();
            list.Add(1);
            list.Add(2);
            list.Add(3);

            var all = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            var first = all.First().Timestamp.UtcDateTime;
            var last = all.Last().Timestamp.UtcDateTime;

            // A range strictly before the first item
            var beforeFrom = first.AddSeconds(-10);
            var beforeTo = first.AddSeconds(-1);

            list.RemoveRange(beforeFrom, beforeTo);

            Assert.Equal(3, list.Count);

            // A range strictly after the last item
            var afterFrom = last.AddSeconds(1);
            var afterTo = last.AddSeconds(10);

            list.RemoveRange(afterFrom, afterTo);

            Assert.Equal(3, list.Count);
        }

        [Fact]
        public void Clear_RemovesAllItems()
        {
            var list = new TemporalSortedList<string>();
            list.Add("a");
            list.Add("b");

            Assert.True(list.Count > 0);

            list.Clear();

            Assert.Equal(0, list.Count);
            Assert.Empty(list.GetInRange(DateTime.MinValue, DateTime.MaxValue));
            Assert.Null(list.GetLatest());
            Assert.Null(list.GetEarliest());
        }

        [Fact]
        public void CountInRange_MatchesGetInRangeCount()
        {
            var list = new TemporalSortedList<int>();
            list.Add(10);
            list.Add(20);
            list.Add(30);

            var all = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            var t1 = all[0].Timestamp.UtcDateTime;
            var t3 = all[2].Timestamp.UtcDateTime;

            var items = list.GetInRange(t1, t3).ToList();
            var count = list.CountInRange(t1, t3);

            Assert.Equal(items.Count, count);
            Assert.Equal(3, count);
        }

        [Fact]
        public void GetTimeSpan_ReturnsZero_ForLessThanTwoItems()
        {
            var list = new TemporalSortedList<int>();
            Assert.Equal(TimeSpan.Zero, list.GetTimeSpan());

            list.Add(1);
            Assert.Equal(TimeSpan.Zero, list.GetTimeSpan());
        }

        [Fact]
        public void GetTimeSpan_IsNonNegative_AndConsistentWithEarliestLatest()
        {
            var list = new TemporalSortedList<int>();
            list.Add(1);
            list.Add(2);
            list.Add(3);

            var span = list.GetTimeSpan();
            Assert.True(span >= TimeSpan.Zero);

            var earliest = list.GetEarliest()!;
            var latest = list.GetLatest()!;
            var expected = latest.Timestamp - earliest.Timestamp;

            // Per implementation, negative spans are clamped to zero; here it must be non-negative.
            Assert.Equal(expected, span);
        }

        [Fact]
        public void GetNearest_OnEmptyList_ReturnsNull()
        {
            var list = new TemporalSortedList<int>();
            Assert.Null(list.GetNearest(DateTime.UtcNow));
        }

        [Fact]
        public void GetNearest_BeforeFirst_ReturnsFirst_AfterLast_ReturnsLast()
        {
            var list = new TemporalSortedList<int>();
            list.Add(1);
            list.Add(2);
            list.Add(3);

            var all = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            var first = all.First();
            var last = all.Last();

            var before = list.GetNearest(first.Timestamp.UtcDateTime.AddTicks(-10));
            var after = list.GetNearest(last.Timestamp.UtcDateTime.AddTicks(10));

            Assert.NotNull(before);
            Assert.NotNull(after);

            Assert.Equal(first.Value, before!.Value);
            Assert.Equal(last.Value, after!.Value);
        }

        [Fact]
        public void RemoveOlderThan_IsExclusive_RemovesStrictlyOlderOnly()
        {
            var list = new TemporalSortedList<int>();
            list.Add(1);
            list.Add(2);
            list.Add(3);

            var all = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            var t2 = all.Single(i => i.Value == 2).Timestamp.UtcDateTime;

            // Remove items with ts < t2 (exclusive), so item "2" must remain.
            list.RemoveOlderThan(t2);

            var remaining = list.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                                .Select(i => i.Value)
                                .ToList();

            Assert.Equal(new[] { 2, 3 }, remaining);
        }

        [Fact]
        public void Add_ManyItems_CountMatches_AndTimestampsAreStrictlyIncreasing()
        {
            var list = new TemporalSortedList<int>();

            for (int i = 0; i < 500; i++)
                list.Add(i);

            Assert.Equal(500, list.Count);

            var items = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            Assert.Equal(500, items.Count);

            for (int i = 1; i < items.Count; i++)
            {
                // TemporalItem<T>.Create guarantees strictly increasing UTC ticks per closed generic type.
                Assert.True(items[i].Timestamp.UtcTicks > items[i - 1].Timestamp.UtcTicks);
            }
        }
    }
}