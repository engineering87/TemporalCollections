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
    }
}