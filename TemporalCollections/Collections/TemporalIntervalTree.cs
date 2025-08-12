// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe interval tree for storing temporal intervals with associated values.
    /// Supports insertion, removal, querying of overlapping intervals and time-based operations via <see cref="ITimeQueryable{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value associated with each interval.</typeparam>
    public class TemporalIntervalTree<T> : ITimeQueryable<T>
    {
        /// <summary>
        /// Represents a single node in the interval tree.
        /// </summary>
        private class Node
        {
            /// <summary>Interval start (inclusive).</summary>
            public DateTime Start;

            /// <summary>Interval end (inclusive).</summary>
            public DateTime End;

            /// <summary>Associated value for the interval.</summary>
            public T Value;

            /// <summary>The maximum end time of this node or any of its descendants.</summary>
            public DateTime MaxEnd;

            public Node? Left;
            public Node? Right;

            /// <summary>
            /// Initializes a new instance of the <see cref="Node"/> class.
            /// </summary>
            /// <param name="start">The start time of the interval.</param>
            /// <param name="end">The end time of the interval (must be >= start).</param>
            /// <param name="value">The value associated with the interval.</param>
            public Node(DateTime start, DateTime end, T value)
            {
                if (end < start)
                    throw new ArgumentException("End must be >= Start", nameof(end));

                Start = start;
                End = end;
                Value = value;
                MaxEnd = end;
            }
        }

        private Node? _root;
        private readonly object _lock = new();

        /// <summary>
        /// Inserts a new interval with an associated value into the tree.
        /// </summary>
        /// <param name="start">The start time of the interval.</param>
        /// <param name="end">The end time of the interval (must be >= start).</param>
        /// <param name="value">The value to associate with the interval.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="end"/> is earlier than <paramref name="start"/>.</exception>
        public void Insert(DateTime start, DateTime end, T value)
        {
            if (end < start)
                throw new ArgumentException("end must be >= start", nameof(end));

            lock (_lock)
            {
                _root = Insert(_root, start, end, value);
            }
        }

        /// <summary>
        /// Removes an interval with the exact same start, end, and value from the tree.
        /// </summary>
        /// <param name="start">The start time of the interval.</param>
        /// <param name="end">The end time of the interval.</param>
        /// <param name="value">The value associated with the interval.</param>
        /// <returns>True if the interval was found and removed; otherwise false.</returns>
        public bool Remove(DateTime start, DateTime end, T value)
        {
            lock (_lock)
            {
                bool removed;
                (_root, removed) = Remove(_root, start, end, value);
                return removed;
            }
        }

        /// <summary>
        /// Returns all values whose intervals overlap with the given query range.
        /// The returned items are wrapped as <see cref="TemporalItem{T}"/> where the item.Timestamp equals the interval Start.
        /// </summary>
        /// <param name="queryStart">The start time of the query interval.</param>
        /// <param name="queryEnd">The end time of the query interval (must be >= queryStart).</param>
        /// <returns>A list of temporal items whose intervals overlap the query interval.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="queryEnd"/> is earlier than <paramref name="queryStart"/>.</exception>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime queryStart, DateTime queryEnd)
        {
            if (queryEnd < queryStart)
                throw new ArgumentException("queryEnd must be >= queryStart", nameof(queryEnd));

            lock (_lock)
            {
                var result = new List<TemporalItem<T>>();
                QueryCollect(_root, queryStart, queryEnd, result);
                return result;
            }
        }

        /// <summary>
        /// Removes all intervals that have already ended strictly before the cutoff (i.e. End &lt; cutoff).
        /// </summary>
        /// <param name="cutoff">The cutoff timestamp; intervals with End &lt; cutoff will be removed.</param>
        public void RemoveOlderThan(DateTime cutoff)
        {
            lock (_lock)
            {
                _root = RemoveOlderThanInternal(_root, cutoff);
            }
        }

        /// <summary>
        /// Returns all values whose intervals overlap with the given query range.
        /// This method keeps the original list-oriented API (returns values only) — useful alongside QueryCollect / direct use.
        /// </summary>
        /// <param name="queryStart">The start of the query interval.</param>
        /// <param name="queryEnd">The end of the query interval.</param>
        /// <returns>A list of values overlapping the query interval.</returns>
        /// <remarks>
        /// This helper retains the original "Query" behavior present in earlier versions.
        /// </remarks>
        public IList<T> Query(DateTime queryStart, DateTime queryEnd)
        {
            if (queryEnd < queryStart)
                throw new ArgumentException("queryEnd must be >= queryStart", nameof(queryEnd));

            lock (_lock)
            {
                var result = new List<T>();
                QueryValues(_root, queryStart, queryEnd, result);
                return result;
            }
        }

        #region Internal recursive helpers

        // Collect TemporalItem<T> for intervals overlapping [qs,qe].
        private void QueryCollect(Node? node, DateTime qs, DateTime qe, List<TemporalItem<T>> result)
        {
            if (node == null) return;

            if (node.Start <= qe && node.End >= qs)
            {
                // Use Start as the "timestamp" for the temporal item (documented choice)
                result.Add(new TemporalItem<T>(node.Value, node.Start));
            }

            if (node.Left != null && node.Left.MaxEnd >= qs)
            {
                QueryCollect(node.Left, qs, qe, result);
            }

            if (node.Right != null && node.Start <= qe)
            {
                QueryCollect(node.Right, qs, qe, result);
            }
        }

        // Collect values for compatibility with older Query method
        private void QueryValues(Node? node, DateTime qs, DateTime qe, List<T> result)
        {
            if (node == null) return;

            if (node.Start <= qe && node.End >= qs)
            {
                result.Add(node.Value);
            }

            if (node.Left != null && node.Left.MaxEnd >= qs)
            {
                QueryValues(node.Left, qs, qe, result);
            }

            if (node.Right != null && node.Start <= qe)
            {
                QueryValues(node.Right, qs, qe, result);
            }
        }

        private Node Insert(Node? node, DateTime start, DateTime end, T value)
        {
            if (node == null)
                return new Node(start, end, value);

            if (start < node.Start)
                node.Left = Insert(node.Left, start, end, value);
            else
                node.Right = Insert(node.Right, start, end, value);

            node.MaxEnd = MaxDate(node.End,
                node.Left?.MaxEnd ?? DateTime.MinValue,
                node.Right?.MaxEnd ?? DateTime.MinValue);

            return node;
        }

        private (Node? node, bool removed) Remove(Node? node, DateTime start, DateTime end, T value)
        {
            if (node == null) return (null, false);

            bool removed = false;

            if (start == node.Start && end == node.End && EqualityComparer<T>.Default.Equals(value, node.Value))
            {
                removed = true;

                // Node with at most one child
                if (node.Left == null) return (node.Right, true);
                if (node.Right == null) return (node.Left, true);

                // Node with two children: replace with inorder successor
                var minNode = FindMin(node.Right);
                node.Start = minNode.Start;
                node.End = minNode.End;
                node.Value = minNode.Value;
                node.Right = RemoveMin(node.Right);
            }
            else if (start < node.Start)
            {
                (node.Left, removed) = Remove(node.Left, start, end, value);
            }
            else
            {
                (node.Right, removed) = Remove(node.Right, start, end, value);
            }

            if (node != null)
            {
                node.MaxEnd = MaxDate(node.End,
                    node.Left?.MaxEnd ?? DateTime.MinValue,
                    node.Right?.MaxEnd ?? DateTime.MinValue);
            }

            return (node, removed);
        }

        // Removes nodes whose End < cutoff and returns new subtree root.
        private Node? RemoveOlderThanInternal(Node? node, DateTime cutoff)
        {
            if (node == null) return null;

            node.Left = RemoveOlderThanInternal(node.Left, cutoff);
            node.Right = RemoveOlderThanInternal(node.Right, cutoff);

            if (node.End < cutoff)
            {
                // delete this node
                if (node.Left == null) return node.Right;
                if (node.Right == null) return node.Left;

                var minNode = FindMin(node.Right);
                node.Start = minNode.Start;
                node.End = minNode.End;
                node.Value = minNode.Value;
                node.Right = RemoveMin(node.Right);
            }

            node.MaxEnd = MaxDate(node.End,
                node.Left?.MaxEnd ?? DateTime.MinValue,
                node.Right?.MaxEnd ?? DateTime.MinValue);

            return node;
        }

        private Node FindMin(Node node)
        {
            while (node.Left != null) node = node.Left;
            return node;
        }

        private Node? RemoveMin(Node node)
        {
            if (node.Left == null)
                return node.Right;

            node.Left = RemoveMin(node.Left);

            node.MaxEnd = MaxDate(node.End,
                node.Left?.MaxEnd ?? DateTime.MinValue,
                node.Right?.MaxEnd ?? DateTime.MinValue);

            return node;
        }

        private static DateTime MaxDate(params DateTime[] dates)
        {
            DateTime max = DateTime.MinValue;
            foreach (var d in dates)
            {
                if (d > max) max = d;
            }
            return max;
        }

        #endregion
    }
}