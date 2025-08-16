// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;

namespace TemporalCollections.Tests.Collections
{
    public class TemporalIntervalTreeTests
    {
        [Fact]
        public void Insert_Throws_WhenEndEarlierThanStart()
        {
            var tree = new TemporalIntervalTree<string>();
            var start = DateTime.UtcNow;
            var end = start.AddSeconds(-1);

            var ex = Assert.Throws<ArgumentException>(() => tree.Insert(start, end, "value"));
            Assert.Contains("end must be >=", ex.Message);
        }

        [Fact]
        public void InsertAndQuery_ReturnsOverlappingIntervals()
        {
            var tree = new TemporalIntervalTree<string>();

            var now = DateTime.UtcNow;
            var i1Start = now;
            var i1End = now.AddMinutes(10);
            var i2Start = now.AddMinutes(5);
            var i2End = now.AddMinutes(15);

            tree.Insert(i1Start, i1End, "interval1");
            tree.Insert(i2Start, i2End, "interval2");

            // Query that overlaps both intervals
            var results = tree.GetInRange(now.AddMinutes(7), now.AddMinutes(12)).ToList();

            Assert.Equal(2, results.Count);
            Assert.Contains(results, item => item.Value == "interval1" && item.Timestamp == i1Start);
            Assert.Contains(results, item => item.Value == "interval2" && item.Timestamp == i2Start);
        }

        [Fact]
        public void Remove_RemovesIntervalSuccessfully()
        {
            var tree = new TemporalIntervalTree<string>();

            var start = DateTime.UtcNow;
            var end = start.AddMinutes(10);

            tree.Insert(start, end, "toRemove");

            Assert.True(tree.Remove(start, end, "toRemove"));

            // After removal, query should return empty
            var results = tree.GetInRange(start, end).ToList();
            Assert.Empty(results);

            // Removing again returns false
            Assert.False(tree.Remove(start, end, "toRemove"));
        }

        [Fact]
        public void RemoveOlderThan_RemovesIntervalsEndingBeforeCutoff()
        {
            var tree = new TemporalIntervalTree<string>();

            var now = DateTime.UtcNow;

            tree.Insert(now.AddMinutes(-20), now.AddMinutes(-10), "old");
            tree.Insert(now.AddMinutes(-5), now.AddMinutes(5), "newer");
            tree.Insert(now.AddMinutes(0), now.AddMinutes(10), "newest");

            var cutoff = now.AddMinutes(-7);
            tree.RemoveOlderThan(cutoff);

            var results = tree.GetInRange(now.AddMinutes(-30), now.AddMinutes(20)).Select(i => i.Value).ToList();

            Assert.DoesNotContain("old", results);
            Assert.Contains("newer", results);
            Assert.Contains("newest", results);
        }

        [Fact]
        public void Query_Throws_WhenQueryEndEarlierThanQueryStart()
        {
            var tree = new TemporalIntervalTree<int>();
            var start = DateTime.UtcNow;
            var end = start.AddMinutes(-1);

            var ex = Assert.Throws<ArgumentException>(() => tree.Query(start, end));
            Assert.Contains("must be <=", ex.Message);
        }

        [Fact]
        public void Query_ReturnsValuesOverlappingInterval()
        {
            var tree = new TemporalIntervalTree<string>();

            var now = DateTime.UtcNow;

            tree.Insert(now, now.AddMinutes(5), "val1");
            tree.Insert(now.AddMinutes(3), now.AddMinutes(10), "val2");
            tree.Insert(now.AddMinutes(15), now.AddMinutes(20), "val3");

            var results = tree.Query(now.AddMinutes(4), now.AddMinutes(14));

            Assert.Contains("val1", results);
            Assert.Contains("val2", results);
            Assert.DoesNotContain("val3", results);
        }

        [Fact]
        public void GetEarliest_And_GetLatest_ShouldReturnIntervalsByStart()
        {
            var tree = new TemporalIntervalTree<string>();
            var baseTime = DateTime.UtcNow;

            var s1 = baseTime.AddMinutes(0);
            var e1 = s1.AddMinutes(10);
            var s2 = baseTime.AddMinutes(5);
            var e2 = s2.AddMinutes(15);
            var s3 = baseTime.AddMinutes(20);
            var e3 = s3.AddMinutes(25);

            tree.Insert(s2, e2, "mid");
            tree.Insert(s1, e1, "early");
            tree.Insert(s3, e3, "late");

            var earliest = tree.GetEarliest();
            var latest = tree.GetLatest();

            Assert.NotNull(earliest);
            Assert.NotNull(latest);

            Assert.Equal("early", earliest!.Value);
            Assert.Equal(s1, earliest.Timestamp);

            Assert.Equal("late", latest!.Value);
            Assert.Equal(s3, latest.Timestamp);
        }

        [Fact]
        public void GetTimeSpan_ShouldBeDifferenceBetweenEarliestAndLatestStart()
        {
            var tree = new TemporalIntervalTree<int>();
            var t0 = new DateTime(2025, 01, 01, 00, 00, 00, DateTimeKind.Utc);
            var t1 = t0.AddMinutes(2);
            var t2 = t0.AddMinutes(7);

            tree.Insert(t0, t0.AddMinutes(1), 1);
            tree.Insert(t1, t1.AddMinutes(1), 2);
            tree.Insert(t2, t2.AddMinutes(1), 3);

            var span = tree.GetTimeSpan();
            Assert.Equal(t2 - t0, span);
        }

        [Fact]
        public void CountInRange_ShouldCountIntervalsWhoseStartIsWithinInclusiveBounds()
        {
            var tree = new TemporalIntervalTree<string>();
            var t0 = new DateTime(2025, 01, 01, 00, 00, 00, DateTimeKind.Utc);
            var t1 = t0.AddMinutes(1);
            var t2 = t0.AddMinutes(2);
            var t3 = t0.AddMinutes(3);

            tree.Insert(t0, t0.AddMinutes(10), "A");
            tree.Insert(t1, t1.AddMinutes(10), "B");
            tree.Insert(t2, t2.AddMinutes(10), "C");
            tree.Insert(t3, t3.AddMinutes(10), "D");

            // Count items with Start in [t1, t2] -> B and C
            var count = tree.CountInRange(t1, t2);
            Assert.Equal(2, count);
        }

        [Fact]
        public void GetBefore_ShouldReturnItemsWithStartStrictlyBeforeTime()
        {
            var tree = new TemporalIntervalTree<string>();
            var t0 = new DateTime(2025, 02, 01, 00, 00, 00, DateTimeKind.Utc);
            var t1 = t0.AddMinutes(5);
            var t2 = t0.AddMinutes(10);

            tree.Insert(t0, t0.AddMinutes(1), "A");
            tree.Insert(t1, t1.AddMinutes(1), "B");
            tree.Insert(t2, t2.AddMinutes(1), "C");

            var before = tree.GetBefore(t1).Select(x => x.Value).ToList(); // strictly before t1
            Assert.Equal(new[] { "A" }, before);
        }

        [Fact]
        public void GetAfter_ShouldReturnItemsWithStartStrictlyAfterTime()
        {
            var tree = new TemporalIntervalTree<string>();
            var t0 = new DateTime(2025, 03, 01, 00, 00, 00, DateTimeKind.Utc);
            var t1 = t0.AddMinutes(5);
            var t2 = t0.AddMinutes(10);

            tree.Insert(t0, t0.AddMinutes(1), "A");
            tree.Insert(t1, t1.AddMinutes(1), "B");
            tree.Insert(t2, t2.AddMinutes(1), "C");

            var after = tree.GetAfter(t1).Select(x => x.Value).ToList(); // strictly after t1
            Assert.Equal(new[] { "C" }, after);
        }

        [Fact]
        public void RemoveRange_ShouldRemoveIntervalsWhoseStartIsWithinInclusiveBounds()
        {
            var tree = new TemporalIntervalTree<string>();
            var baseTime = DateTime.UtcNow;

            var sA = baseTime.AddMinutes(0);
            var eA = sA.AddMinutes(30);
            var sB = baseTime.AddMinutes(5);
            var eB = sB.AddMinutes(30);
            var sC = baseTime.AddMinutes(10);
            var eC = sC.AddMinutes(30);
            var sD = baseTime.AddMinutes(25);
            var eD = sD.AddMinutes(30);

            tree.Insert(sA, eA, "A");
            tree.Insert(sB, eB, "B");
            tree.Insert(sC, eC, "C");
            tree.Insert(sD, eD, "D");

            // Remove by Start in [sB, sC] -> removes B and C
            tree.RemoveRange(sB, sC);

            var remaining = tree.GetInRange(baseTime.AddMinutes(-1), baseTime.AddMinutes(100))
                                .Select(x => x.Value)
                                .ToList();

            Assert.Contains("A", remaining);
            Assert.Contains("D", remaining);
            Assert.DoesNotContain("B", remaining);
            Assert.DoesNotContain("C", remaining);
        }

        [Fact]
        public void Clear_ShouldEmptyTreeAndResetQueryableState()
        {
            var tree = new TemporalIntervalTree<int>();
            var t0 = DateTime.UtcNow;
            tree.Insert(t0, t0.AddMinutes(1), 1);
            tree.Insert(t0.AddMinutes(2), t0.AddMinutes(3), 2);

            tree.Clear();

            Assert.Equal(TimeSpan.Zero, tree.GetTimeSpan());
            Assert.Null(tree.GetEarliest());
            Assert.Null(tree.GetLatest());
            Assert.Empty(tree.GetInRange(DateTime.MinValue, DateTime.MaxValue));
        }

        [Fact]
        public void Insert_ShouldBeThreadSafe_WhenManyParallelInserts()
        {
            var tree = new TemporalIntervalTree<int>();
            var baseTime = DateTime.UtcNow;

            // Insert 1000 intervals concurrently with non-overlapping starts
            Parallel.For(0, 1000, i =>
            {
                var start = baseTime.AddSeconds(i);
                var end = start.AddSeconds(10);
                tree.Insert(start, end, i);
            });

            // Count intervals by start range equals number inserted
            var count = tree.CountInRange(baseTime, baseTime.AddSeconds(999));
            Assert.Equal(1000, count);

            // Earliest and latest sanity check
            var earliest = tree.GetEarliest();
            var latest = tree.GetLatest();
            Assert.NotNull(earliest);
            Assert.NotNull(latest);
            Assert.Equal(baseTime, earliest!.Timestamp);
            Assert.Equal(baseTime.AddSeconds(999), latest!.Timestamp);
        }
    }
}