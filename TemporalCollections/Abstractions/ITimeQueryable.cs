// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Models;

namespace TemporalCollections.Abstractions
{
    /// <summary>
    /// Defines methods for querying and managing items based on their timestamps.
    /// </summary>
    public interface ITimeQueryable<T>
    {
        /// <summary>
        /// Returns items whose timestamps fall within the given range (inclusive).
        /// </summary>
        IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to);

        /// <summary>
        /// Removes all items older than the specified cutoff date.
        /// </summary>
        void RemoveOlderThan(DateTime cutoff);
    }
}