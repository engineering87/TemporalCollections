// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)

namespace TemporalCollections.Utilities
{
    /// <summary>
    /// Defines how <see cref="DateTimeKind.Unspecified"/> should be handled
    /// when normalizing to a <see cref="DateTimeOffset"/> in UTC.
    /// </summary>
    public enum UnspecifiedPolicy
    {
        /// <summary>
        /// Reject Unspecified values (throw an exception).
        /// </summary>
        Reject = 0,

        /// <summary>
        /// Treat Unspecified as UTC (offset = 0).
        /// </summary>
        AssumeUtc = 1,

        /// <summary>
        /// Treat Unspecified as Local and convert to UTC.
        /// </summary>
        AssumeLocal = 2
    }

    /// <summary>
    /// Provides helpers for converting <see cref="DateTime"/> inputs
    /// into <see cref="DateTimeOffset"/> values normalized to UTC.
    /// Includes consistent handling for <see cref="DateTimeKind"/>.
    /// </summary>
    public static class TimeNormalization
    {
        /// <summary>
        /// Converts a <see cref="DateTime"/> into a <see cref="DateTimeOffset"/> in UTC.
        /// Utc -> preserved as UTC; Local -> converted to UTC;
        /// Unspecified -> handled according to <paramref name="unspecifiedPolicy"/>.
        /// </summary>
        /// <param name="dt">The input <see cref="DateTime"/>.</param>
        /// <param name="paramName">Parameter name for exception messages.</param>
        /// <param name="unspecifiedPolicy">How to handle <see cref="DateTimeKind.Unspecified"/>.</param>
        /// <returns>A <see cref="DateTimeOffset"/> with offset 0 (UTC).</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="dt"/> has Kind=Unspecified and policy=Reject.
        /// </exception>
        public static DateTimeOffset ToUtcOffset(
            DateTime dt,
            string paramName,
            UnspecifiedPolicy unspecifiedPolicy = UnspecifiedPolicy.Reject)
        {
            return dt.Kind switch
            {
                DateTimeKind.Utc => new DateTimeOffset(dt, TimeSpan.Zero),
                DateTimeKind.Local => new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero),
                DateTimeKind.Unspecified => unspecifiedPolicy switch
                {
                    UnspecifiedPolicy.AssumeUtc => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero),
                    UnspecifiedPolicy.AssumeLocal => new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt)).ToUniversalTime(),
                    _ => throw new ArgumentException(
                            $"{paramName} must have Kind=Utc or Local (Unspecified is not allowed).",
                            paramName)
                },
                _ => throw new ArgumentOutOfRangeException(paramName)
            };
        }

        /// <summary>
        /// Nullable overload: returns null if <paramref name="dt"/> is null.
        /// </summary>
        public static DateTimeOffset? ToUtcOffset(
            DateTime? dt,
            string paramName,
            UnspecifiedPolicy unspecifiedPolicy = UnspecifiedPolicy.Reject)
            => dt.HasValue ? ToUtcOffset(dt.Value, paramName, unspecifiedPolicy) : (DateTimeOffset?)null;

        /// <summary>
        /// Attempts to convert a <see cref="DateTime"/> into UTC using the same policy.
        /// Returns false if the conversion fails (e.g., Unspecified + Reject).
        /// </summary>
        public static bool TryToUtcOffset(
            DateTime dt,
            out DateTimeOffset utc,
            UnspecifiedPolicy unspecifiedPolicy = UnspecifiedPolicy.Reject)
        {
            try { utc = ToUtcOffset(dt, nameof(dt), unspecifiedPolicy); return true; }
            catch { utc = default; return false; }
        }

        /// <summary>
        /// Returns the UTC ticks of a <see cref="DateTime"/> under the specified policy.
        /// </summary>
        public static long UtcTicks(DateTime dt, UnspecifiedPolicy unspecifiedPolicy = UnspecifiedPolicy.Reject)
            => ToUtcOffset(dt, nameof(dt), unspecifiedPolicy).UtcTicks;

        /// <summary>
        /// Nullable overload: returns null if input is null.
        /// </summary>
        public static long? UtcTicks(DateTime? dt, UnspecifiedPolicy unspecifiedPolicy = UnspecifiedPolicy.Reject)
            => dt.HasValue ? UtcTicks(dt.Value, unspecifiedPolicy) : (long?)null;

        /// <summary>
        /// Normalizes a [from, to] range to UTC and validates that from &lt;= to.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when from &gt; to after normalization.
        /// </exception>
        public static (DateTimeOffset fromUtc, DateTimeOffset toUtc) NormalizeRange(
            DateTime from, DateTime to,
            string fromName = "from", string toName = "to",
            UnspecifiedPolicy unspecifiedPolicy = UnspecifiedPolicy.Reject)
        {
            var f = ToUtcOffset(from, fromName, unspecifiedPolicy);
            var t = ToUtcOffset(to, toName, unspecifiedPolicy);
            if (f > t)
                throw new ArgumentException($"'{fromName}' must be <= '{toName}' (in UTC).");
            return (f, t);
        }

        /// <summary>
        /// Checks if a timestamp is in the inclusive range [from, to].
        /// </summary>
        public static bool IsInRangeInclusive(
            DateTimeOffset tsUtc,
            DateTime from,
            DateTime to,
            UnspecifiedPolicy unspecifiedPolicy = UnspecifiedPolicy.Reject)
        {
            var (f, t) = NormalizeRange(from, to, unspecifiedPolicy: unspecifiedPolicy);
            var x = tsUtc.UtcTicks;
            return f.UtcTicks <= x && x <= t.UtcTicks;
        }

        /// <summary>
        /// Checks if a timestamp is strictly before the given DateTime.
        /// </summary>
        public static bool IsBeforeExclusive(
            DateTimeOffset tsUtc,
            DateTime time,
            UnspecifiedPolicy unspecifiedPolicy = UnspecifiedPolicy.Reject)
            => tsUtc.UtcTicks < UtcTicks(time, unspecifiedPolicy);

        /// <summary>
        /// Checks if a timestamp is strictly after the given DateTime.
        /// </summary>
        public static bool IsAfterExclusive(
            DateTimeOffset tsUtc,
            DateTime time,
            UnspecifiedPolicy unspecifiedPolicy = UnspecifiedPolicy.Reject)
            => tsUtc.UtcTicks > UtcTicks(time, unspecifiedPolicy);
    }
}