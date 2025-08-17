// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Reflection;
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

        [Fact]
        public void Insert_ExactDuplicate_ShouldBeNoOp()
        {
            var tree = new TemporalIntervalTree<string>();
            var s = DateTime.UtcNow;
            var e = s.AddMinutes(10);

            tree.Insert(s, e, "X");
            tree.Insert(s, e, "X"); // exact duplicate (Start, End, Value)

            // Only one item overlaps
            var results = tree.GetInRange(s.AddMinutes(1), e.AddMinutes(-1)).ToList();
            Assert.Single(results);
            Assert.Equal("X", results[0].Value);

            // Removing once should succeed, second time should fail
            Assert.True(tree.Remove(s, e, "X"));
            Assert.False(tree.Remove(s, e, "X"));
        }

        [Fact]
        public void Insert_SameStartEnd_DifferentValues_ShouldCreateTwoDistinctNodes()
        {
            var tree = new TemporalIntervalTree<string>();
            var s = DateTime.UtcNow;
            var e = s.AddMinutes(5);

            tree.Insert(s, e, "A");
            tree.Insert(s, e, "B"); // same temporal key, different value

            var values = tree.Query(s, e).OrderBy(x => x).ToList();
            Assert.Equal(2, values.Count);
            Assert.Equal(new[] { "A", "B" }, values);
        }

        [Fact]
        public void Query_NoOverlap_ReturnsEmpty()
        {
            var tree = new TemporalIntervalTree<int>();
            var s = DateTime.UtcNow;
            tree.Insert(s, s.AddMinutes(5), 1);

            var result = tree.Query(s.AddMinutes(6), s.AddMinutes(7));
            Assert.Empty(result);
        }

        [Fact]
        public void Remove_NotFound_ReturnsFalse()
        {
            var tree = new TemporalIntervalTree<int>();
            var s = DateTime.UtcNow;

            tree.Insert(s, s.AddMinutes(1), 1);
            var ok = tree.Remove(s, s.AddMinutes(1), 2); // value differs
            Assert.False(ok);

            ok = tree.Remove(s.AddMinutes(1), s.AddMinutes(2), 1); // time differs
            Assert.False(ok);
        }

        [Fact]
        public void RemoveOlderThan_CutoffEqualToEnd_ShouldNotRemove()
        {
            var tree = new TemporalIntervalTree<string>();
            var s = DateTime.UtcNow;
            var e = s.AddMinutes(10);

            tree.Insert(s, e, "keep");

            // Condition is End < cutoff (strict). Using cutoff == e should keep it.
            tree.RemoveOlderThan(e);
            var res = tree.Query(s, e);
            Assert.Contains("keep", res);
        }

        [Fact]
        public void GetBefore_And_GetAfter_BoundariesAreStrict()
        {
            var tree = new TemporalIntervalTree<string>();
            var t = new DateTime(2025, 04, 01, 12, 00, 00, DateTimeKind.Utc);

            tree.Insert(t.AddMinutes(-1), t.AddMinutes(1), "beforeEq");
            tree.Insert(t, t.AddMinutes(1), "equal");
            tree.Insert(t.AddMinutes(1), t.AddMinutes(2), "afterEq");

            var before = tree.GetBefore(t).Select(x => x.Value).ToList();
            var after = tree.GetAfter(t).Select(x => x.Value).ToList();

            Assert.DoesNotContain("equal", before);
            Assert.Contains("beforeEq", before);

            Assert.DoesNotContain("equal", after);
            Assert.Contains("afterEq", after);
        }

        [Fact]
        public void RemoveRange_BoundariesAreInclusive()
        {
            var tree = new TemporalIntervalTree<string>();
            var baseTime = DateTime.UtcNow;

            var s1 = baseTime.AddMinutes(0); var e1 = s1.AddMinutes(10);
            var s2 = baseTime.AddMinutes(5); var e2 = s2.AddMinutes(10);
            var s3 = baseTime.AddMinutes(10); var e3 = s3.AddMinutes(10);

            tree.Insert(s1, e1, "A");
            tree.Insert(s2, e2, "B");
            tree.Insert(s3, e3, "C");

            // Remove inclusive of bounds -> should remove A (s1) and C (s3) as well if included
            tree.RemoveRange(s1, s3);

            var remaining = tree.GetInRange(baseTime.AddMinutes(-1), baseTime.AddMinutes(30))
                                .Select(x => x.Value).ToList();

            Assert.Empty(remaining); // all removed
        }

        [Fact]
        public void ManyEqualStarts_ShouldStillBehaveCorrectly()
        {
            var tree = new TemporalIntervalTree<int>();
            var s = new DateTime(2025, 05, 01, 0, 0, 0, DateTimeKind.Utc);

            // Same Start for all, End strictly increasing; Values unique
            for (int i = 0; i < 1000; i++)
                tree.Insert(s, s.AddSeconds(i), i);

            // CountInRange over the single start equals 1000
            Assert.Equal(1000, tree.CountInRange(s, s));

            // A range that overlaps all should retrieve 1000 values
            var vals = tree.Query(s, s.AddHours(1));
            Assert.Equal(1000, vals.Count);
        }

        [Fact]
        public void RemoveRange_All_ShouldClearTree()
        {
            var tree = new TemporalIntervalTree<int>();
            var baseTime = DateTime.UtcNow;

            for (int i = 0; i < 50; i++)
                tree.Insert(baseTime.AddSeconds(i), baseTime.AddSeconds(i + 10), i);

            tree.RemoveRange(baseTime.AddSeconds(0), baseTime.AddSeconds(49));
            Assert.Empty(tree.GetInRange(DateTime.MinValue, DateTime.MaxValue));
            Assert.Null(tree.GetEarliest());
            Assert.Null(tree.GetLatest());
            Assert.Equal(TimeSpan.Zero, tree.GetTimeSpan());
        }

        [Fact]
        public void Fuzzy_RandomizedIntervals_MatchesNaiveOverlapLogic()
        {
            var rnd = new Random(12345);
            var tree = new TemporalIntervalTree<int>();

            var baseTime = new DateTime(2025, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var items = new List<(DateTime s, DateTime e, int v)>();

            // generate 100 random intervals within 0..1000 seconds
            for (int i = 0; i < 100; i++)
            {
                var a = rnd.Next(0, 1000);
                var b = rnd.Next(0, 1000);
                var s = baseTime.AddSeconds(Math.Min(a, b));
                var e = baseTime.AddSeconds(Math.Max(a, b));
                var v = i;
                items.Add((s, e, v));
                tree.Insert(s, e, v);
            }

            // 20 random queries, compare against naïve overlap
            for (int q = 0; q < 20; q++)
            {
                var a = rnd.Next(0, 1000);
                var b = rnd.Next(0, 1000);
                var qs = baseTime.AddSeconds(Math.Min(a, b));
                var qe = baseTime.AddSeconds(Math.Max(a, b));

                var expected = items.Where(t => t.s <= qe && t.e >= qs).Select(t => t.v).OrderBy(x => x).ToList();
                var actual = tree.Query(qs, qe).OrderBy(x => x).ToList();

                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void Concurrency_ManyParallelInsertsWithSameStart()
        {
            var tree = new TemporalIntervalTree<int>();
            var s = DateTime.UtcNow;

            Parallel.For(0, 2000, i =>
            {
                var end = s.AddMilliseconds(i % 50 + 1); // small spread on End
                tree.Insert(s, end, i);
            });

            // All inserted should be present
            Assert.Equal(2000, tree.CountInRange(s, s));

            var vals = tree.Query(s, s.AddSeconds(2)).ToList();
            Assert.Equal(2000, vals.Count);
        }

        [Fact]
        public void Treap_PriorityHeapInvariant_IsMaintained()
        {
            // Build a non-trivial treap
            var tree = new TemporalIntervalTree<int>();
            var baseTime = DateTime.UtcNow;

            for (int i = 0; i < 300; i++)
            {
                var s = baseTime.AddSeconds(i);
                var e = s.AddSeconds(10 + (i % 5));
                tree.Insert(s, e, i);
            }

            // Reflect into private root field
            var treeType = typeof(TemporalIntervalTree<int>);
            var rootField = treeType.GetField("_root", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(rootField);

            var root = rootField!.GetValue(tree);
            Assert.NotNull(root);

            // Resolve node fields; support both public and non-public
            var nodeType = root!.GetType();
            const BindingFlags AnyInst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var fLeft = nodeType.GetField("Left", AnyInst);
            var fRight = nodeType.GetField("Right", AnyInst);
            var fPriority = nodeType.GetField("Priority", AnyInst);

            Assert.NotNull(fLeft);
            Assert.NotNull(fRight);
            Assert.NotNull(fPriority);

            bool Check(object? n)
            {
                if (n is null) return true;

                var pObj = fPriority!.GetValue(n);
                Assert.NotNull(pObj);
                int p = (int)pObj!;

                var left = fLeft!.GetValue(n);
                var right = fRight!.GetValue(n);

                if (left is not null)
                {
                    var lpObj = fPriority!.GetValue(left);
                    Assert.NotNull(lpObj);
                    int lp = (int)lpObj!;
                    Assert.True(p <= lp, "Treap min-heap invariant violated on left child.");
                    if (!Check(left)) return false;
                }

                if (right is not null)
                {
                    var rpObj = fPriority!.GetValue(right);
                    Assert.NotNull(rpObj);
                    int rp = (int)rpObj!;
                    Assert.True(p <= rp, "Treap min-heap invariant violated on right child.");
                    if (!Check(right)) return false;
                }

                return true;
            }

            Assert.True(Check(root));
        }

        [Fact]
        public void RemoveOlderThan_ShouldRemoveAll_WhenAllEndBeforeCutoff()
        {
            var tree = new TemporalIntervalTree<int>();
            var baseTime = DateTime.UtcNow;

            for (int i = 0; i < 20; i++)
            {
                var s = baseTime.AddMinutes(-60 + i);
                var e = s.AddMinutes(1);
                tree.Insert(s, e, i);
            }

            var cutoff = baseTime.AddMinutes(-30);
            tree.RemoveOlderThan(cutoff);

            Assert.Empty(tree.GetInRange(DateTime.MinValue, DateTime.MaxValue));
            Assert.Equal(0, tree.CountInRange(DateTime.MinValue, DateTime.MaxValue));
        }

        [Fact]
        public void Mixed_InsertRemoveQuery_ShouldRemainConsistent()
        {
            var tree = new TemporalIntervalTree<string>();
            var t0 = new DateTime(2025, 06, 01, 0, 0, 0, DateTimeKind.Utc);

            // Insert 10, remove even-indexed, then query
            for (int i = 0; i < 10; i++)
            {
                var s = t0.AddMinutes(i);
                var e = s.AddMinutes(2);
                tree.Insert(s, e, $"V{i}");
            }

            for (int i = 0; i < 10; i += 2)
            {
                var s = t0.AddMinutes(i);
                var e = s.AddMinutes(2);
                Assert.True(tree.Remove(s, e, $"V{i}"));
            }

            // Only odd remain
            var all = tree.GetInRange(t0.AddMinutes(-1), t0.AddMinutes(20)).Select(x => x.Value).OrderBy(x => x).ToList();
            var expected = Enumerable.Range(0, 10).Where(i => i % 2 == 1).Select(i => $"V{i}").OrderBy(x => x).ToList();

            Assert.Equal(expected, all);
        }
    }
}