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
            Assert.Contains("queryEnd must be >=", ex.Message);
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
    }
}