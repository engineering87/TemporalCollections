// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Collections.Concurrent;
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe dictionary storing multiple timestamped values per key,
    /// supporting temporal range queries and cleanup of old entries.
    /// Implements ITimeQueryable to query over all keys.
    /// </summary>
    /// <typeparam name="TKey">The type of keys in the dictionary (not nullable).</typeparam>
    /// <typeparam name="TValue">The type of values stored with timestamps.</typeparam>
    public class TemporalDictionary<TKey, TValue> : ITimeQueryable<KeyValuePair<TKey, TValue>>
        where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, List<TemporalItem<TValue>>> _dict = new();

        /// <summary>
        /// Adds a new value associated with the specified key, timestamped with the current UTC time.
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            var temporalItem = TemporalItem<TValue>.Create(value);
            var list = _dict.GetOrAdd(key, _ => new List<TemporalItem<TValue>>());
            lock (list)
            {
                list.Add(temporalItem);
            }
        }

        /// <summary>
        /// Retrieves all temporal items associated with the specified <paramref name="key"/> whose timestamps
        /// fall within the inclusive range from <paramref name="from"/> to <paramref name="to"/>.
        /// </summary>
        /// <param name="key">The key to look up temporal items for.</param>
        /// <param name="from">The start of the timestamp range (inclusive).</param>
        /// <param name="to">The end of the timestamp range (inclusive).</param>
        /// <returns>
        /// A collection of <see cref="TemporalItem{TValue}"/> instances matching the key and timestamp range.
        /// Returns an empty collection if the key is not found or no items fall within the range.
        /// </returns>
        // Aggiungi validazione e ordering per la variante per-chiave
        public IEnumerable<TemporalItem<TValue>> GetInRange(TKey key, DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("to must be >= from", nameof(to));

            if (_dict.TryGetValue(key, out var list))
            {
                lock (list)
                {
                    // Order by timestamp to provide deterministic results
                    return list
                        .Where(item => item.Timestamp >= from && item.Timestamp <= to)
                        .OrderBy(item => item.Timestamp)
                        .ToList();
                }
            }
            return [];
        }

        /// <summary>
        /// Removes all timestamped values older than the specified cutoff date from all keys.
        /// </summary>
        public void RemoveOlderThan(DateTime cutoff)
        {
            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    list.RemoveAll(item => item.Timestamp < cutoff);
                    if (list.Count == 0)
                        _dict.TryRemove(kvp.Key, out _);
                }
            }
        }

        /// <summary>
        /// Gets all keys currently present in the dictionary.
        /// </summary>
        public IEnumerable<TKey> Keys => _dict.Keys;

        /// <summary>
        /// Gets the number of keys currently stored in the dictionary.
        /// </summary>
        public int Count => _dict.Count;

        #region ITimeQueryable<KeyValuePair<TKey,TValue>> implementation

        /// <summary>
        /// Retrieves all temporal items stored in the dictionary whose timestamps
        /// fall within the inclusive range from <paramref name="from"/> to <paramref name="to"/>.
        /// Each item returned is wrapped as a <see cref="TemporalItem{T}"/> containing
        /// a <see cref="KeyValuePair{TKey, TValue}"/> with the original key and value.
        /// The collections are locked individually during enumeration for thread safety.
        /// </summary>
        /// <param name="from">The start of the timestamp range (inclusive).</param>
        /// <param name="to">The end of the timestamp range (inclusive).</param>
        /// <returns>
        /// A list of <see cref="TemporalItem{KeyValuePair{TKey, TValue}}"/> instances
        /// where each item's timestamp is within the specified range.
        /// </returns>
        public IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> GetInRange(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("to must be >= from", nameof(to));

            var results = new List<TemporalItem<KeyValuePair<TKey, TValue>>>();

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    foreach (var item in list)
                    {
                        if (item.Timestamp >= from && item.Timestamp <= to)
                        {
                            var pair = new KeyValuePair<TKey, TValue>(kvp.Key, item.Value);
                            results.Add(new TemporalItem<KeyValuePair<TKey, TValue>>(pair, item.Timestamp));
                        }
                    }
                }
            }

            // Ensure deterministic ordering across keys
            results.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return results;
        }

        #endregion

        /// <summary>
        /// Returns the time span between the earliest and the latest timestamp across all stored items.
        /// Returns <see cref="TimeSpan.Zero"/> if the dictionary is empty.
        /// </summary>
        public TimeSpan GetTimeSpan()
        {
            bool any = false;
            DateTime min = DateTime.MaxValue;
            DateTime max = DateTime.MinValue;

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    foreach (var it in list)
                    {
                        any = true;
                        var ts = it.Timestamp;
                        if (ts < min) min = ts;
                        if (ts > max) max = ts;
                    }
                }
            }

            if (!any || min >= max) return TimeSpan.Zero;
            var span = max - min;
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }

        /// <summary>
        /// Counts how many items across all keys have a timestamp within the inclusive range
        /// from <paramref name="from"/> to <paramref name="to"/>.
        /// </summary>
        public int CountInRange(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("to must be >= from", nameof(to));

            var count = 0;
            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    count += list.Count(i => i.Timestamp >= from && i.Timestamp <= to);
                }
            }
            return count;
        }

        /// <summary>
        /// Removes all keys and all their timestamped values from the dictionary.
        /// </summary>
        public void Clear()
        {
            _dict.Clear();
        }

        /// <summary>
        /// Removes all items whose timestamps fall within the inclusive range
        /// from <paramref name="from"/> to <paramref name="to"/> across all keys.
        /// Keys left with no items are removed as well.
        /// </summary>
        public void RemoveRange(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("to must be >= from", nameof(to));

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    list.RemoveAll(i => i.Timestamp >= from && i.Timestamp <= to);
                    if (list.Count == 0)
                        _dict.TryRemove(kvp.Key, out _);
                }
            }
        }

        /// <summary>
        /// Returns the latest (most recent) item across all keys, or <c>null</c> if empty.
        /// The returned item contains the original key/value as a <see cref="KeyValuePair{TKey,TValue}"/>.
        /// </summary>
        public TemporalItem<KeyValuePair<TKey, TValue>>? GetLatest()
        {
            DateTime bestTs = DateTime.MinValue;
            TKey? bestKey = default!;
            TValue? bestVal = default!;
            bool found = false;

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    foreach (var it in list)
                    {
                        if (!found || it.Timestamp > bestTs)
                        {
                            bestTs = it.Timestamp;
                            bestKey = kvp.Key;
                            bestVal = it.Value;
                            found = true;
                        }
                    }
                }
            }

            return found
                ? new TemporalItem<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(bestKey, bestVal), bestTs)
                : null;
        }

        /// <summary>
        /// Returns the earliest (oldest) item across all keys, or <c>null</c> if empty.
        /// The returned item contains the original key/value as a <see cref="KeyValuePair{TKey,TValue}"/>.
        /// </summary>
        public TemporalItem<KeyValuePair<TKey, TValue>>? GetEarliest()
        {
            DateTime bestTs = DateTime.MaxValue;
            TKey? bestKey = default!;
            TValue? bestVal = default!;
            bool found = false;

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    foreach (var it in list)
                    {
                        if (!found || it.Timestamp < bestTs)
                        {
                            bestTs = it.Timestamp;
                            bestKey = kvp.Key;
                            bestVal = it.Value;
                            found = true;
                        }
                    }
                }
            }

            return found
                ? new TemporalItem<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(bestKey, bestVal), bestTs)
                : null;
        }

        /// <summary>
        /// Retrieves all items strictly before the specified <paramref name="time"/> across all keys.
        /// The returned items wrap the original key/value.
        /// </summary>
        public IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> GetBefore(DateTime time)
        {
            var results = new List<TemporalItem<KeyValuePair<TKey, TValue>>>();

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    foreach (var item in list)
                    {
                        if (item.Timestamp < time)
                        {
                            results.Add(new TemporalItem<KeyValuePair<TKey, TValue>>(
                                new KeyValuePair<TKey, TValue>(kvp.Key, item.Value),
                                item.Timestamp));
                        }
                    }
                }
            }

            results.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return results;
        }

        /// <summary>
        /// Retrieves all items strictly after the specified <paramref name="time"/> across all keys.
        /// The returned items wrap the original key/value.
        /// </summary>
        public IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> GetAfter(DateTime time)
        {
            var results = new List<TemporalItem<KeyValuePair<TKey, TValue>>>();

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    foreach (var item in list)
                    {
                        if (item.Timestamp > time)
                        {
                            results.Add(new TemporalItem<KeyValuePair<TKey, TValue>>(
                                new KeyValuePair<TKey, TValue>(kvp.Key, item.Value),
                                item.Timestamp));
                        }
                    }
                }
            }

            results.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return results;
        }
    }
}