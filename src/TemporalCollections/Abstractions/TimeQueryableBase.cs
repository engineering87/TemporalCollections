// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Models;
using TemporalCollections.Utilities;

namespace TemporalCollections.Abstractions
{
    /// <summary>
    /// Base class centralizing logic on DateTimeOffset. 
    /// DateTime overloads delegate via TimeNormalization.
    /// </summary>
    public abstract class TimeQueryableBase<T> : ITimeQueryable<T>
    {
        /// <summary>
        /// Policy for handling DateTimeKind.Unspecified in DateTime overloads.
        /// Override if you want a different behavior (e.g., AssumeUtc or AssumeLocal).
        /// </summary>
        protected virtual UnspecifiedPolicy UnspecifiedPolicyForDateTime => UnspecifiedPolicy.AssumeUtc;

        /// <summary>
        /// Returns items whose timestamps fall within the given range (inclusive).
        /// </summary>
        public abstract IEnumerable<TemporalItem<T>> GetInRange(DateTimeOffset from, DateTimeOffset to);

        /// <summary>
        /// Removes all items older than the specified cutoff date/time (strictly earlier than <paramref name="cutoff"/>).
        /// </summary>
        public abstract void RemoveOlderThan(DateTimeOffset cutoff);

        /// <summary>
        /// Returns the number of items in the specified time range (inclusive).
        /// </summary>
        public abstract int CountInRange(DateTimeOffset from, DateTimeOffset to);

        /// <summary>
        /// Removes all items whose timestamps fall within the specified range [from, to].
        /// </summary>
        public abstract void RemoveRange(DateTimeOffset from, DateTimeOffset to);

        /// <summary>
        /// Retrieves all items with timestamp strictly before the specified time.
        /// </summary>
        public abstract IEnumerable<TemporalItem<T>> GetBefore(DateTimeOffset time);

        /// <summary>
        /// Retrieves all items with timestamp strictly after the specified time.
        /// </summary>
        public abstract IEnumerable<TemporalItem<T>> GetAfter(DateTimeOffset time);

        /// <summary>
        /// Counts the number of items with timestamp greater than or equal to the specified cutoff.
        /// </summary>
        public abstract int CountSince(DateTimeOffset from);

        /// <summary>
        /// Retrieves the item whose timestamp is closest to the specified <paramref name="time"/>.
        /// Returns <c>null</c> if the collection is empty.
        /// </summary>
        public abstract TemporalItem<T>? GetNearest(DateTimeOffset time);

        /// <summary>
        /// Returns the total timespan covered by items in the collection
        /// (difference between earliest and latest timestamp), or <see cref="TimeSpan.Zero"/> if empty.
        /// </summary>
        public abstract TimeSpan GetTimeSpan();

        /// <summary>
        /// Retrieves the latest item based on timestamp, or null if empty.
        /// </summary>
        public abstract TemporalItem<T>? GetLatest();

        /// <summary>
        /// Retrieves the earliest item based on timestamp, or null if empty.
        /// </summary>
        public abstract TemporalItem<T>? GetEarliest();

        /// <summary>
        /// Removes all items from the collection.
        /// </summary>
        public abstract void Clear();

        /// <summary>
        /// Returns items whose timestamps fall within the given range (inclusive).
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            var (f, t) = TimeNormalization.NormalizeRange(
                from, to,
                unspecifiedPolicy: UnspecifiedPolicyForDateTime);
            return GetInRange(f, t);
        }

        /// <summary>
        /// Removes all items older than the specified cutoff date.
        /// </summary>
        public void RemoveOlderThan(DateTime cutoff)
        {
            var c = TimeNormalization.ToUtcOffset(
                cutoff, nameof(cutoff), UnspecifiedPolicyForDateTime);
            RemoveOlderThan(c);
        }

        /// <summary>
        /// Returns the number of items in the specified time range.
        /// </summary>
        public int CountInRange(DateTime from, DateTime to)
        {
            var (f, t) = TimeNormalization.NormalizeRange(
                from, to,
                unspecifiedPolicy: UnspecifiedPolicyForDateTime);
            return CountInRange(f, t);
        }

        /// <summary>
        /// Removes all items whose timestamps fall within the specified range [from, to].
        /// </summary>
        public void RemoveRange(DateTime from, DateTime to)
        {
            var (f, t) = TimeNormalization.NormalizeRange(
                from, to,
                unspecifiedPolicy: UnspecifiedPolicyForDateTime);
            RemoveRange(f, t);
        }

        /// <summary>
        /// Retrieves all items with timestamp before the specified time (exclusive).
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetBefore(DateTime time)
        {
            var t = TimeNormalization.ToUtcOffset(
                time, nameof(time), UnspecifiedPolicyForDateTime);
            return GetBefore(t);
        }

        /// <summary>
        /// Retrieves all items with timestamp after the specified time (exclusive).
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetAfter(DateTime time)
        {
            var t = TimeNormalization.ToUtcOffset(
                time, nameof(time), UnspecifiedPolicyForDateTime);
            return GetAfter(t);
        }

        /// <summary>
        /// Counts the number of items with timestamp greater than or equal to the specified cutoff.
        /// </summary>
        public int CountSince(DateTime from)
        {
            var f = TimeNormalization.ToUtcOffset(
                from, nameof(from), UnspecifiedPolicyForDateTime);
            return CountSince(f);
        }

        /// <summary>
        /// Retrieves the item whose timestamp is closest to the specified time.
        /// Returns <c>null</c> if the collection is empty.
        /// </summary>
        public TemporalItem<T>? GetNearest(DateTime time)
        {
            var t = TimeNormalization.ToUtcOffset(
                time, nameof(time), UnspecifiedPolicyForDateTime);
            return GetNearest(t);
        }
    }
}