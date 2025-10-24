// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Runtime.CompilerServices;

namespace TemporalCollections.Models
{
    /// <summary>
    /// Immutable value paired with a UTC timestamp (<see cref="DateTimeOffset"/>).
    /// - Timestamps are guaranteed to be strictly increasing monotonic *per closed generic type*,
    ///   even under high concurrency or same-tick creations.
    /// - UTC is used to avoid time zone / DST ambiguity at the API boundary.
    /// </summary>
    /// <typeparam name="T">Wrapped value type.</typeparam>
    public record TemporalItem<T>(T Value, DateTimeOffset Timestamp)
    {
        // Tracks the last emitted UTC ticks for *this closed generic type*.
        // We use DateTimeOffset.UtcNow.Ticks (100-ns tick resolution) and ensure
        // strictly increasing values via an atomic Compare-Exchange loop.
        // Initialize with current UtcNow ticks to avoid starting at zero.
        private static long _lastUtcTicks = DateTimeOffset.UtcNow.UtcTicks;

        // Legacy shim (UTC)
        public DateTime TimestampUtc => Timestamp.UtcDateTime;

        /// <summary>
        /// Creates a new item stamped with a strictly increasing UTC timestamp.
        /// If multiple items are created within the same tick or concurrently,
        /// we increment ticks to preserve strict ordering.
        /// </summary>
        /// <param name="value">Value to wrap.</param>
        /// <returns>New <see cref="TemporalItem{T}"/> with monotonic UTC timestamp.</returns>
        public static TemporalItem<T> Create(T value)
        {
            // Current wall clock in UTC ticks (offset = 0).
            long nowTicks = DateTimeOffset.UtcNow.UtcTicks;

            long observed; // last observed ticks
            long next;     // candidate next ticks (>= observed + 1)

            // Ensure strictly increasing ticks using an atomic CAS loop:
            //   next = max(nowTicks, observed + 1)
            do
            {
                observed = Volatile.Read(ref _lastUtcTicks);
                next = nowTicks <= observed ? observed + 1 : nowTicks;
            }
            while (Interlocked.CompareExchange(ref _lastUtcTicks, next, observed) != observed);

            // Construct a UTC DateTimeOffset from ticks (offset zero).
            return new TemporalItem<T>(value, new DateTimeOffset(next, TimeSpan.Zero));
        }

        /// <summary>
        /// Stable comparer: primary order by <see cref="Timestamp"/> ascending,
        /// then tie-break on <see cref="Value"/> when comparable, and finally on
        /// runtime identity to maintain a strict weak ordering.
        /// </summary>
        public static IComparer<TemporalItem<T>> TimestampComparer { get; } = new StableTimestampComparer();

        private sealed class StableTimestampComparer : IComparer<TemporalItem<T>>
        {
            public int Compare(TemporalItem<T>? x, TemporalItem<T>? y)
            {
                // Handle nulls / reference-equals quickly
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;

                // Primary: timestamp (UTC)
                int c = x.Timestamp.UtcTicks.CompareTo(y.Timestamp.UtcTicks);
                if (c != 0) return c;

                // Secondary: value when comparable.
                // Prefer generic IComparable<T> for strong typing, then fallback to non-generic.
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

                // Final: runtime identity to keep a strict ordering and avoid
                // dropping distinct items from sorted sets/maps.
                int hx = RuntimeHelpers.GetHashCode(x);
                int hy = RuntimeHelpers.GetHashCode(y);
                return hx.CompareTo(hy);
            }
        }
    }
}