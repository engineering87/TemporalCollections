// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;
using TemporalCollections.Extensions;

namespace TemporalCollections.Tests.Collections
{
    public class TemporalValueExtensionsTests
    {
        // Helper: creates a TemporalQueue<int> with values 1..n (timestamps auto-assigned, chronological == insertion order)
        private static TemporalQueue<int> MakeIntQueue(params int[] values)
        {
            var q = new TemporalQueue<int>();
            foreach (var v in values) q.Enqueue(v);
            return q;
        }

        [Fact]
        public void ToValueList_ReturnsAllValuesInChronologicalOrder()
        {
            var source = MakeIntQueue(1, 2, 3, 4);

            var list = source.ToValueList();

            Assert.Equal(new[] { 1, 2, 3, 4 }, list);
            // Materialized: modifications to the result do not affect source
            list.Add(99);
            Assert.Equal(4, source.CountInRange(DateTime.MinValue, DateTime.MaxValue));
        }

        [Fact]
        public void ToValueArray_ReturnsAllValuesInChronologicalOrder()
        {
            var source = MakeIntQueue(10, 20, 30);

            var arr = source.ToValueArray();

            Assert.Equal(new[] { 10, 20, 30 }, arr);
        }

        [Fact]
        public void ToValueHashSet_ContainsUniqueValues_IgnoreOrder()
        {
            // Requires: public HashSet<T> ToValueHashSet<T>(..., IEqualityComparer<T>? comparer = null)
            var source = MakeIntQueue(1, 2, 2, 3, 3, 3);

            var set = source.ToValueHashSet();

            Assert.Equal(3, set.Count);
            Assert.True(set.SetEquals(new[] { 1, 2, 3 }));
        }

        [Fact]
        public void ToValueDictionary_UsesKeySelector_LastWinsOnDuplicates()
        {
            // Requires: public Dictionary<TKey,T> ToValueDictionary<T,TKey>(..., Func<T,TKey> keySelector, IEqualityComparer<TKey>? comparer = null)
            // Key selector: parity (even/odd) so we force collisions.
            var source = MakeIntQueue(1, 2, 4, 6, 3, 8); // last even = 8, last odd = 3

            var dict = source.ToValueDictionary(v => v % 2 == 0 ? "even" : "odd");

            Assert.Equal(2, dict.Count);
            Assert.Equal(8, dict["even"]);
            Assert.Equal(3, dict["odd"]);
        }

        [Fact]
        public void ToValueQueue_MaterializesFifoSequence()
        {
            // Requires: public Queue<T> ToValueQueue<T>(this ITimeQueryable<T> source)
            var source = MakeIntQueue(5, 6, 7);

            var q = source.ToValueQueue();

            Assert.Equal(new[] { 5, 6, 7 }, q.ToArray());
            // Dequeue should follow FIFO
            Assert.Equal(5, q.Dequeue());
            Assert.Equal(6, q.Dequeue());
            Assert.Equal(7, q.Dequeue());
        }

        [Fact]
        public void ToValueStack_MaterializesLifoSequence()
        {
            // Requires: public Stack<T> ToValueStack<T>(this ITimeQueryable<T> source)
            var source = MakeIntQueue(1, 2, 3);

            var s = source.ToValueStack();

            // Stack enumerates from top to bottom (LIFO): 3,2,1
            Assert.Equal(new[] { 3, 2, 1 }, s.ToArray());
            Assert.Equal(3, s.Pop());
            Assert.Equal(2, s.Pop());
            Assert.Equal(1, s.Pop());
        }

        [Fact]
        public void ToReadOnlyValueCollection_MaterializesAndIsReadOnly()
        {
            // Requires: public IReadOnlyCollection<T> ToReadOnlyValueCollection<T>(this ITimeQueryable<T> source)
            var source = MakeIntQueue(9, 8, 7);

            var ro = source.ToReadOnlyValueCollection();

            Assert.IsAssignableFrom<IReadOnlyCollection<int>>(ro);
            Assert.Equal(3, ro.Count);
            Assert.Contains(9, ro);
            Assert.Contains(8, ro);
            Assert.Contains(7, ro);

            // Verify it is actually read-only (ReadOnlyCollection<T> implements ICollection<T> but Add throws)
            var asCollection = ro as ICollection<int>;
            Assert.NotNull(asCollection);
            Assert.True(asCollection!.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => asCollection.Add(42));
        }

        [Fact]
        public void BucketBy_GroupsIntoSingleDailyBucket_AndAppliesAggregator()
        {
            var source = MakeIntQueue(1, 2, 3, 4);

            var earliest = source.GetEarliest();
            Assert.NotNull(earliest);
            var dayStartUtc = new DateTimeOffset(earliest!.Timestamp.UtcDateTime.Date, TimeSpan.Zero);

            var buckets = source
                .BucketBy<int, int>(
                    interval: TimeSpan.FromDays(1),
                    aggregator: items => items.Sum(i => i.Value)
                )
                .ToList();

            Assert.Single(buckets);
            Assert.Equal(dayStartUtc, buckets[0].BucketStart);
            Assert.Equal(1 + 2 + 3 + 4, buckets[0].Result);
        }
    }
}
