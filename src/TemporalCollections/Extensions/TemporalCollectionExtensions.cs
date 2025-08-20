// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;

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
        /// Later duplicates override earlier ones.
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
    }
}