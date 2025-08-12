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
        /// Retrieves all timestamped values for the specified key whose timestamps fall within the inclusive range [from, to].
        /// </summary>
        public IEnumerable<TemporalItem<TValue>> GetInRange(TKey key, DateTime from, DateTime to)
        {
            if (_dict.TryGetValue(key, out var list))
            {
                lock (list)
                {
                    return list
                        .Where(item => item.Timestamp >= from && item.Timestamp <= to)
                        .ToList();
                }
            }
            return Enumerable.Empty<TemporalItem<TValue>>();
        }

        /// <summary>
        /// Removes all timestamped values older than the specified cutoff date from all keys.
        /// </summary>
        public void RemoveOlderThan(DateTime cutoff)
        {
            foreach (var key in _dict.Keys)
            {
                if (_dict.TryGetValue(key, out var list))
                {
                    lock (list)
                    {
                        list.RemoveAll(item => item.Timestamp < cutoff);
                        if (list.Count == 0)
                        {
                            _dict.TryRemove(key, out _);
                        }
                    }
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

        /// <inheritdoc/>
        public IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> GetInRange(DateTime from, DateTime to)
        {
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

            return results;
        }

        #endregion
    }
}