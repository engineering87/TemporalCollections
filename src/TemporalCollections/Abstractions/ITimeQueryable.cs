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
        IEnumerable<TemporalItem<T>> GetInRange(DateTimeOffset from, DateTimeOffset to);

        /// <summary>
        /// Removes all items older than the specified cutoff date/time (strictly earlier than <paramref name="cutoff"/>).
        /// </summary>
        void RemoveOlderThan(DateTimeOffset cutoff);

        /// <summary>
        /// Returns the number of items in the specified time range (inclusive).
        /// </summary>
        int CountInRange(DateTimeOffset from, DateTimeOffset to);

        /// <summary>
        /// Removes all items whose timestamps fall within the specified range [from, to].
        /// </summary>
        void RemoveRange(DateTimeOffset from, DateTimeOffset to);

        /// <summary>
        /// Retrieves all items with timestamp strictly before the specified time.
        /// </summary>
        IEnumerable<TemporalItem<T>> GetBefore(DateTimeOffset time);

        /// <summary>
        /// Retrieves all items with timestamp strictly after the specified time.
        /// </summary>
        IEnumerable<TemporalItem<T>> GetAfter(DateTimeOffset time);

        /// <summary>
        /// Counts the number of items with timestamp greater than or equal to the specified cutoff.
        /// </summary>
        int CountSince(DateTimeOffset from);

        /// <summary>
        /// Retrieves the item whose timestamp is closest to the specified <paramref name="time"/>.
        /// Returns <c>null</c> if the collection is empty.
        /// </summary>
        TemporalItem<T>? GetNearest(DateTimeOffset time);

        /// <summary>
        /// Returns the total timespan covered by items in the collection
        /// (difference between earliest and latest timestamp), or <see cref="TimeSpan.Zero"/> if empty.
        /// </summary>
        TimeSpan GetTimeSpan();

        /// <summary>
        /// Retrieves the latest item based on timestamp, or null if empty.
        /// </summary>
        TemporalItem<T>? GetLatest();

        /// <summary>
        /// Retrieves the earliest item based on timestamp, or null if empty.
        /// </summary>
        TemporalItem<T>? GetEarliest();

        /// <summary>
        /// Removes all items from the collection.
        /// </summary>
        void Clear();

        /// <summary>
        /// Returns items whose timestamps fall within the given range (inclusive).
        /// </summary>
        IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to);

        /// <summary>
        /// Removes all items older than the specified cutoff date.
        /// </summary>
        void RemoveOlderThan(DateTime cutoff);

        /// <summary>
        /// Returns the number of items in the specified time range.
        /// </summary>
        int CountInRange(DateTime from, DateTime to);

        /// <summary>
        /// Removes all items whose timestamps fall within the specified range [from, to].
        /// </summary>
        void RemoveRange(DateTime from, DateTime to);

        /// <summary>
        /// Retrieves all items with timestamp before the specified time (exclusive).
        /// </summary>
        IEnumerable<TemporalItem<T>> GetBefore(DateTime time);

        /// <summary>
        /// Retrieves all items with timestamp after the specified time (exclusive).
        /// </summary>
        IEnumerable<TemporalItem<T>> GetAfter(DateTime time);

        /// <summary>
        /// Counts the number of items with timestamp greater than or equal to the specified cutoff.
        /// </summary>
        int CountSince(DateTime from);

        /// <summary>
        /// Retrieves the item whose timestamp is closest to the specified time.
        /// Returns <c>null</c> if the collection is empty.
        /// </summary>
        TemporalItem<T>? GetNearest(DateTime time);
    }
}