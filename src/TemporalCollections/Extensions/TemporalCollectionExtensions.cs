// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Extensions
{
    /// <summary>
    /// Non-mutating, value-centric extensions for ITimeQueryable{T}.
    /// These helpers never re-insert elements, so original timestamps are preserved.
    /// </summary>
    public static class TemporalValueExtensions
    {
        /// <summary>
        /// Materializes all values (chronological order) into a List.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when source is null.</exception>
        public static List<T> ToValueList<T>(this ITimeQueryable<T> source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return source
                .GetInRange(DateTime.MinValue, DateTime.MaxValue)
                .Select(i => i.Value)
                .ToList();
        }

        /// <summary>
        /// Materializes all values (chronological order) into an array.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when source is null.</exception>
        public static T[] ToValueArray<T>(this ITimeQueryable<T> source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return source
                .GetInRange(DateTime.MinValue, DateTime.MaxValue)
                .Select(i => i.Value)
                .ToArray();
        }

        /// <summary>
        /// Materializes all values into a HashSet (chronological order is lost).
        /// </summary>
        public static HashSet<T> ToValueHashSet<T>(
            this ITimeQueryable<T> source,
            IEqualityComparer<T>? comparer = null)
        {
            ArgumentNullException.ThrowIfNull(source);

            return comparer is null
                ? source.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                        .Select(i => i.Value)
                        .ToHashSet()
                : source.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                        .Select(i => i.Value)
                        .ToHashSet(comparer);
        }

        /// <summary>
        /// Materializes all values into a Dictionary using a key selector.
        /// When duplicate keys occur, the value from the latest item (by timestamp) overwrites earlier ones.
        /// </summary>
        public static Dictionary<TKey, T> ToValueDictionary<T, TKey>(
            this ITimeQueryable<T> source,
            Func<T, TKey> keySelector,
            IEqualityComparer<TKey>? comparer = null)
            where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(keySelector);

            var dict = comparer is null ? []
                                        : new Dictionary<TKey, T>(comparer);

            foreach (var v in source.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(i => i.Value))
            {
                var key = keySelector(v);
                dict[key] = v; // last wins: overwrite any previous value for the same key
            }

            return dict;
        }

        /// <summary>
        /// Materializes all values into a Queue (FIFO, chronological order).
        /// </summary>
        public static Queue<T> ToValueQueue<T>(this ITimeQueryable<T> source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return new Queue<T>(
                source.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(i => i.Value));
        }

        /// <summary>
        /// Materializes all values into a Stack (LIFO, last value on top).
        /// </summary>
        public static Stack<T> ToValueStack<T>(this ITimeQueryable<T> source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return new Stack<T>(
                source.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(i => i.Value));
        }

        /// <summary>
        /// Materializes all values into a ReadOnlyCollection for safe exposure.
        /// </summary>
        public static IReadOnlyCollection<T> ToReadOnlyValueCollection<T>(this ITimeQueryable<T> source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return source.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                         .Select(i => i.Value)
                         .ToList()
                         .AsReadOnly();
        }

        /// <summary>
        /// Groups all temporal items into fixed-size time buckets (e.g., per minute, hour, day)
        /// and applies a custom aggregation function to each bucket.
        /// </summary>
        /// <typeparam name="T">Underlying value type.</typeparam>
        /// <typeparam name="TResult">Type produced by the aggregation function.</typeparam>
        /// <param name="source">Temporal data source to group.</param>
        /// <param name="interval">
        /// Bucket duration (e.g., <c>TimeSpan.FromMinutes(1)</c> for 1-minute buckets).
        /// Must be strictly greater than zero.
        /// </param>
        /// <param name="aggregator">
        /// Function that receives all <see cref="TemporalItem{T}"/> instances belonging to a bucket
        /// and returns an aggregated result (e.g., average, sum, latest value).
        /// </param>
        /// <param name="alignment">
        /// Optional reference time used to align bucket boundaries.
        /// Defaults to <see cref="DateTimeOffset.UnixEpoch"/> if not provided.
        /// </param>
        /// <returns>
        /// An ordered sequence of tuples containing the bucket start time and the aggregation result
        /// for that bucket, sorted chronologically.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="source"/> or <paramref name="aggregator"/> is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="interval"/> is less than or equal to zero.
        /// </exception>
        public static IEnumerable<(DateTimeOffset BucketStart, TResult Result)> BucketBy<T, TResult>(
            this ITimeQueryable<T> source,
            TimeSpan interval,
            Func<IReadOnlyList<TemporalItem<T>>, TResult> aggregator,
            DateTimeOffset? alignment = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(aggregator);
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));

            var align = alignment ?? DateTimeOffset.UnixEpoch;

            static DateTimeOffset FloorTo(DateTimeOffset ts, DateTimeOffset align, TimeSpan step)
            {
                var delta = ts - align;
                var buckets = (long)Math.Floor(delta.Ticks / (double)step.Ticks);
                return align.AddTicks(buckets * step.Ticks);
            }

            var groups = new SortedDictionary<DateTimeOffset, List<TemporalItem<T>>>();
            foreach (var item in source.GetInRange(DateTime.MinValue, DateTime.MaxValue))
            {
                var key = FloorTo(item.Timestamp, align, interval);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<TemporalItem<T>>();
                    groups[key] = list;
                }
                list.Add(item);
            }

            foreach (var (bucket, items) in groups)
                yield return (bucket, aggregator(items));
        }
    }
}