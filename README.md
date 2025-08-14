# TemporalCollections

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![issues - wart](https://img.shields.io/github/issues/engineering87/TemporalCollections)](https://github.com/engineering87/TemporalCollections/issues)
[![stars - wart](https://img.shields.io/github/stars/engineering87/TemporalCollections?style=social)](https://github.com/engineering87/TemporalCollections)
[![Sponsor me](https://img.shields.io/badge/Sponsor-❤-pink)](https://github.com/sponsors/engineering87)

**TemporalCollections** is a high-performance, thread-safe .NET library providing temporal data structures. Each structure associates items with precise insertion timestamps, enabling efficient time-based querying, filtering, and cleanup.

This project is ideal for scenarios where you need to store, query, and manage data with temporal semantics — such as event streams, time-windowed analytics, caching with expiry, or temporal state tracking.

## Overview

TemporalCollections provides multiple thread-safe generic collections where each item is timestamped at insertion using a strictly monotonic clock. These collections expose interfaces for querying items based on their timestamps, removing old or expired entries efficiently, and preserving concurrency guarantees.

The key design goals are:

- **Temporal semantics:** Items are stored with precise insertion timestamps
- **Thread safety:** Suitable for concurrent multi-threaded environments
- **Time-based querying:** Fast retrieval of items within time ranges
- **Efficient cleanup:** Removing stale or expired data without locking entire collections for long

## Core Concept: `TemporalItem<T>`

At the heart of all collections lies the `TemporalItem<T>` struct:

- Wraps an immutable value `T` with a timestamp (`DateTime`) indicating the moment of insertion
- Guarantees strictly increasing timestamps even under rapid or concurrent creation, using atomic operations
- Provides a timestamp comparer for sorting and searching

## Available Collections
| Collection Name             | Description                                                                                          | Thread Safety | Ordering            | Key Features                                           |
|-----------------------------|--------------------------------------------------------------------------------------------------|---------------|---------------------|-------------------------------------------------------|
| TemporalQueue<T>            | Thread-safe FIFO queue with timestamped items. Supports enqueue, dequeue, peek, time-range query.| Yes           | FIFO (timestamp)    | Efficient time-range retrieval, remove old items.     |
| TemporalStack<T>            | Thread-safe LIFO stack with timestamped items. Allows push, pop, peek, and time-based cleanup.   | Yes           | LIFO (timestamp)    | Time-range queries, O(n) removal of old elements.     |
| TemporalSet<T>              | Thread-safe set of unique items timestamped at insertion. Supports add, contains, remove, queries.| Yes          | Unordered           | Unique items, time-range query, remove old items.     |
| TemporalSlidingWindowSet<T> | Thread-safe set retaining only items within a sliding time window. Automatically cleans expired items.| Yes        | Unordered           | Sliding window expiration, efficient removal.         |
| TemporalSortedList<T>       | Thread-safe sorted list of timestamped items. Maintains chronological order, supports binary search.| Yes         | Sorted by timestamp | Efficient range queries, sorted order guaranteed.     |
| TemporalPriorityQueue<T>    | Thread-safe priority queue with timestamped items. Supports priority-based dequeueing and queries.| Yes           | Priority order      | Priority-based ordering with time queries.             |
| TemporalIntervalTree<T>     | Thread-safe interval tree for timestamped intervals. Efficient overlap queries and interval removals.| Yes         | Interval-based      | Efficient interval overlap queries and removals.       |
| TemporalDictionary<TKey, TValue> | Thread-safe dictionary where each key maps to a timestamped value. Supports add/update, remove, and time queries.| Yes | Unordered           | Key-based access with timestamp tracking and queries. |
| TemporalCircularBuffer<T>   | Thread-safe fixed-size circular buffer with timestamped items. Overwrites oldest items on overflow.| Yes           | FIFO (circular)     | Fixed size, efficient overwriting and time queries.    |

## Usage Guidance
| Collection Name             | When to Use                                                                                         | When Not to Use                                            |
|-----------------------------|--------------------------------------------------------------------------------------------------|------------------------------------------------------------|
| TemporalQueue<T>            | When you need a thread-safe FIFO queue with time-based retrieval and cleanup.                     | If you need priority ordering or random access.            |
| TemporalStack<T>            | When you want a thread-safe LIFO stack with timestamp tracking and time-range queries.           | If you require fast arbitrary removal or sorting by timestamp. |
| TemporalSet<T>              | When you need unique timestamped items with efficient membership checks and time-based removal.  | If you require ordering of elements or priority queues.     |
| TemporalSlidingWindowSet<T> | When you want to automatically retain only recent items within a fixed time window.              | If your window size is highly variable or if you need sorted access. |
| TemporalSortedList<T>       | When you need a sorted collection by timestamp with efficient range queries.                      | If insertions are very frequent and performance is critical (due to list shifting). |
| TemporalPriorityQueue<T>    | When priority-based ordering with timestamp tracking is required for dequeueing.                 | If you only need FIFO or LIFO semantics without priorities. |
| TemporalIntervalTree<T>     | When you need efficient interval overlap queries and interval-based time operations.             | If your data are single points rather than intervals.       |
| TemporalDictionary<TKey, TValue> | When key-based access combined with timestamp tracking and querying is needed.              | If ordering or range queries by timestamp are required.     |
| TemporalCircularBuffer<T>   | When you want a fixed-size buffer that overwrites oldest items with timestamp tracking.          | If you need unbounded storage or complex queries.           |

## ITimeQueryable<T> Interface

All temporal collections implement the `ITimeQueryable<T>` interface, which provides a common set of methods to query and manage items based on their associated timestamps. This interface enables consistent time-based operations across different collection types.

### Key Methods

- **GetInRange(DateTime from, DateTime to)**  
  Returns an enumerable of temporal items whose timestamps fall within the inclusive range `[from, to]`. This allows filtering the collection by any desired time window.

- **RemoveOlderThan(DateTime cutoff)**  
  Removes all items with timestamps strictly older than the specified `cutoff` time (`Timestamp < cutoff`). This method is useful for pruning outdated data and maintaining collection size.

- **CountInRange(DateTime from, DateTime to)**  
  Returns the number of items with timestamps in the inclusive range `[from, to]`. Throws if to < from.

- **GetTimeSpan()**  
  Returns `latest.Timestamp - earliest.Timestamp`. Returns `TimeSpan.Zero` if the collection is empty or has a single item.

- **Clear()**  
  Removes all items from the collection.

- **RemoveRange(DateTime from, DateTime to)**  
  Removes all items with timestamps in the inclusive range `[from, to]`. Throws if `to < from`.

- **GetLatest()**  
  Returns the most recent item (max timestamp), or null if empty.

- **GetEarliest()**  
  Returns the oldest item (min timestamp), or null if empty.

- **GetBefore(DateTime time)**  
  Returns all items with `Timestamp < time` (strictly before), ordered by ascending timestamp.

- **GetAfter(DateTime time)**  
  Returns all items with `Timestamp > time` (strictly after), ordered by ascending timestamp.

These methods collectively support efficient and thread-safe temporal queries and cleanups, allowing each collection to manage its items according to their timestamps while exposing a unified API.

## Monotonic Timestamp Guarantee
A key feature of the temporal collections is the guarantee that timestamps assigned to items are strictly monotonically increasing, even when multiple items are created concurrently or in rapid succession.

This is achieved through the `TemporalItem<T>` record, which uses an atomic compare-and-swap operation on a static internal timestamp counter. When creating a new temporal item, the current UTC timestamp in ticks is retrieved and compared against the last assigned timestamp:

- If the current timestamp is greater than the last one, it is used as-is.
- If the current timestamp is less than or equal to the last assigned timestamp (e.g., due to rapid creation or clock precision limits), the timestamp is artificially incremented by one tick.

This approach ensures:

- **Uniqueness:** No two items share the exact same timestamp.
- **Strict ordering:** Timestamps always increase in time order.
- **Thread safety:** The mechanism works correctly across multiple threads without race conditions.

By enforcing this monotonic timestamp ordering, the temporal collections can rely on consistent time-based queries and maintain correct chronological order of items.

## Notes
- **Deterministic ordering**: query results are returned in ascending timestamp order.
- **Snapshot semantics**: methods that return enumerables/lists provide a stable snapshot at call time.
- **Thread-safety**: all operations are designed to be thread-safe per collection.
- **Intervals**: for interval-based collections, the Timestamp used by this interface refers to the interval start.

### Contributing
Thank you for considering to help out with the source code!
If you'd like to contribute, please fork, fix, commit and send a pull request for the maintainers to review and merge into the main code base.

**Getting started with Git and GitHub**

 * [Setting up Git](https://docs.github.com/en/get-started/getting-started-with-git/set-up-git)
 * [Fork the repository](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/working-with-forks/fork-a-repo)
 * [Open an issue](https://github.com/engineering87/TemporalCollections/issues) if you encounter a bug or have a suggestion for improvements/features

### Licensee
TemporalCollections source code is available under MIT License, see license in the source.

### Contact
Please contact at francesco.delre[at]protonmail.com for any details.
