// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)

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
        /// Provides an <see cref="IComparer{T}"/> for comparing two temporal items
        /// based on their <see cref="Timestamp"/> values.
        /// </summary>
        public static IComparer<TemporalItem<T>> TimestampComparer { get; } = new TimestampComparerImpl();

        /// <summary>
        /// Private comparer implementation that compares <see cref="TemporalItem{T}"/> instances
        /// by their timestamps in ascending order.
        /// </summary>
        private class TimestampComparerImpl : IComparer<TemporalItem<T>>
        {
            /// <inheritdoc />
            public int Compare(TemporalItem<T>? x, TemporalItem<T>? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;
                return x.Timestamp.CompareTo(y.Timestamp);
            }
        }
    }
}