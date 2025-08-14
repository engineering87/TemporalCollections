// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Runtime.CompilerServices;

namespace TemporalCollections.Models
{
    /// <summary>
    /// Represents an immutable value paired with the timestamp of its creation.
    /// Ensures that timestamps are strictly increasing, even when multiple items
    /// are created in rapid succession or in parallel threads.
    /// </summary>
    /// <typeparam name="T">The type of the value being wrapped.</typeparam>
    public record TemporalItem<T>(T Value, DateTime Timestamp)
    {
        // Stores the last timestamp (in ticks) that was generated.
        // Used to guarantee uniqueness and monotonicity across calls to Create().
        private static long _lastTicks = DateTime.UtcNow.Ticks;

        /// <summary>
        /// Creates a new <see cref="TemporalItem{T}"/> instance with the current UTC timestamp.
        /// If multiple items are created within the same tick, the timestamp will be artificially
        /// incremented to ensure uniqueness and strict ordering.
        /// </summary>
        /// <param name="value">The value to be wrapped in a temporal container.</param>
        /// <returns>A new <see cref="TemporalItem{T}"/> with a guaranteed unique timestamp.</returns>
        public static TemporalItem<T> Create(T value)
        {
            long ticks = DateTime.UtcNow.Ticks;
            long original, updated;

            // Loop until we successfully update the last used timestamp atomically.
            do
            {
                original = _lastTicks;
                // If the current ticks are not greater than the last used, increment by 1 tick
                updated = ticks <= original ? original + 1 : ticks;
            }
            while (Interlocked.CompareExchange(ref _lastTicks, updated, original) != original);

            return new TemporalItem<T>(value, new DateTime(updated, DateTimeKind.Utc));
        }

        /// <summary>
        /// IComparer that orders items by Timestamp ascending and uses stable tie-breakers
        /// to avoid treating distinct items as duplicates in ordered sets.
        /// </summary>
        public static IComparer<TemporalItem<T>> TimestampComparer { get; } = new StableTimestampComparer();

        private sealed class StableTimestampComparer : IComparer<TemporalItem<T>>
        {
            public int Compare(TemporalItem<T>? x, TemporalItem<T>? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;

                // Primary: timestamp
                int c = x.Timestamp.CompareTo(y.Timestamp);
                if (c != 0) return c;

                // Secondary: value if comparable (generic first)
                if (x.Value is IComparable<T> gen && y.Value is not null)
                {
                    c = gen.CompareTo(y.Value);
                    if (c != 0) return c;
                }
                else if (x.Value is IComparable nong && y.Value is not null)
                {
                    c = nong.CompareTo(y.Value);
                    if (c != 0) return c;
                }

                // Final: runtime identity to guarantee a strict weak ordering
                // (prevents SortedSet from dropping distinct items as duplicates)
                int hx = RuntimeHelpers.GetHashCode(x);
                int hy = RuntimeHelpers.GetHashCode(y);
                if (hx != hy) return hx.CompareTo(hy);

                // Extremely unlikely: same runtime identity (or same reference)
                return 0;
            }
        }
    }
}