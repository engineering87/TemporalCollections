# TemporalCollections

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Nuget](https://img.shields.io/nuget/v/TemporalCollections?style=plastic)](https://www.nuget.org/packages/TemporalCollections)
![NuGet Downloads](https://img.shields.io/nuget/dt/TemporalCollections)
[![issues - temporalcollections](https://img.shields.io/github/issues/engineering87/TemporalCollections)](https://github.com/engineering87/TemporalCollections/issues)
[![CodeQL](https://github.com/engineering87/TemporalCollections/actions/workflows/codeql.yml/badge.svg)](https://github.com/engineering87/TemporalCollections/actions/workflows/codeql.yml)
[![stars - temporalcollections](https://img.shields.io/github/stars/engineering87/TemporalCollections?style=social)](https://github.com/engineering87/TemporalCollections)
[![Sponsor me](https://img.shields.io/badge/Sponsor-❤-pink)](https://github.com/sponsors/engineering87)

**TemporalCollections** is a high-performance, thread-safe .NET library providing temporal data structures. Each structure associates items with precise insertion timestamps, enabling efficient time-based querying, filtering, and cleanup.
This project is ideal for scenarios where you need to store, query, and manage data with temporal semantics, such as event streams, time-windowed analytics, caching with expiry, or temporal state tracking.

## Table of Contents
- [Overview](#overview)
- [Core Concept: `TemporalItem<T>`](#core-concept-temporalitemt)
- [Available Collections](#available-collections)
- [Usage Guidance](#usage-guidance)
- [ITimeQueryable<T> Interface](#itimequeryable-interface)
  - [Key Methods](#key-methods)
- [Getting Started with TemporalCollections](#-getting-started-with-temporalcollections)
  - [Installation](#installation)
  - [Basic usage](#basic-usage)
  - [Common queries via `ITimeQueryable<T>`](#common-queries-via-itimequeryablet)
- [Monotonic Timestamp Guarantee](#monotonic-timestamp-guarantee)
- [Performance Benchmarks](#-performance-benchmarks)
- [Threading Model & Big-O Cheatsheet](#threading-model--big-o-cheatsheet)
- [Notes](#notes)
- [Contributing](#contributing)
- [License](#license)
- [Contact](#contact)

## Overview

TemporalCollections provides multiple thread-safe generic collections where Each item is timestamped at insertion using a strictly monotonic UTC clock (`DateTimeOffset.UtcNow`). These collections expose interfaces for querying items based on their timestamps, removing old or expired entries efficiently, and preserving concurrency guarantees.

The key design goals are:

- **Temporal semantics:** Items are stored with precise insertion timestamps
- **Thread safety:** Suitable for concurrent multi-threaded environments
- **Time-based querying:** Fast retrieval of items within time ranges
- **Efficient cleanup:** Removing stale or expired data without locking entire collections for long

## Core Concept: `TemporalItem<T>`

At the heart of all collections lies the `TemporalItem<T>` struct:

- Wraps an immutable value `T` with a timestamp (`DateTimeOffset`) indicating the moment of insertion
- Guarantees strictly increasing timestamps even under rapid or concurrent creation, using atomic operations
- Provides a timestamp comparer for sorting and searching

## Available Collections
| Collection Name                  | Description                                                                                               | Thread Safety | Ordering                    | Key Features                                                         |
|----------------------------------|-----------------------------------------------------------------------------------------------------------|---------------|-----------------------------|-----------------------------------------------------------------------|
| TemporalQueue<T>                 | Thread-safe FIFO queue with timestamped items. Supports enqueue, dequeue, peek, time-range query.       | Yes           | FIFO (timestamp)            | Efficient time-range retrieval, remove old items.                     |
| TemporalStack<T>                 | Thread-safe LIFO stack with timestamped items. Allows push, pop, peek, and time-based cleanup.          | Yes           | LIFO (timestamp)            | Time-range queries, O(n) removal of old elements.                     |
| TemporalSet<T>                   | Thread-safe set of unique items timestamped at insertion. Supports add, contains, remove, queries.      | Yes           | Unordered                   | Unique items, time-range query, remove old items.                     |
| TemporalSlidingWindowSet<T>      | Thread-safe set retaining only items within a sliding time window. Automatically cleans expired items.  | Yes           | Unordered                   | Sliding window expiration, efficient removal.                         |
| TemporalSortedList<T>            | Thread-safe sorted list of timestamped items. Maintains chronological order, supports binary search.    | Yes           | Sorted by timestamp         | Efficient range queries, sorted order guaranteed.                     |
| TemporalSegmentedArray<T>        | Thread-safe time-ordered segmented array optimized for append-in-order workloads and retention.         | Yes           | Sorted by timestamp (global) | Amortized O(1) append in-order, segment-based range queries and cleanup. |
| TemporalPriorityQueue<T>         | Thread-safe priority queue with timestamped items. Supports priority-based dequeueing and queries.      | Yes           | Priority order              | Priority-based ordering with time queries.                            |
| TemporalIntervalTree<T>          | Thread-safe interval tree for timestamped intervals. Efficient overlap queries and interval removals.   | Yes           | Interval-based              | Efficient interval overlap queries and removals.                      |
| TemporalDictionary<TKey, TValue> | Thread-safe dictionary where each key maps to a timestamped value. Supports add/update, remove, queries.| Yes           | Unordered                   | Key-based access with timestamp tracking and queries.                 |
| TemporalMultimap<TKey, TValue>   | Thread-safe multimap where each key maps to multiple timestamped values with global time-based queries. | Yes           | Per-key chronological       | Multiple values per key, per-key range queries and global time view.  |
| TemporalCircularBuffer<T>        | Thread-safe fixed-size circular buffer with timestamped items. Overwrites oldest items on overflow.     | Yes           | FIFO (circular)             | Fixed size, efficient overwriting and time queries.                   |

## Usage Guidance
| Collection Name                  | When to Use                                                                                                       | When Not to Use                                                                                  |
|----------------------------------|-------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------|
| TemporalQueue<T>                 | When you need a thread-safe FIFO queue with time-based retrieval and cleanup.                                     | If you need priority ordering or random access.                                                  |
| TemporalStack<T>                 | When you want a thread-safe LIFO stack with timestamp tracking and time-range queries.                            | If you require fast arbitrary removal or sorting by timestamp.                                   |
| TemporalSet<T>                   | When you need unique timestamped items with efficient membership checks and time-based removal.                  | If you require ordering of elements or priority queues.                                          |
| TemporalSlidingWindowSet<T>      | When you want to automatically retain only recent items within a fixed time window.                               | If your window size is highly variable or if you need sorted access.                            |
| TemporalSortedList<T>            | When you need a sorted collection by timestamp with efficient range queries.                                      | If insertions are very frequent and performance is critical (due to list shifting).             |
| TemporalSegmentedArray<T>        | When you ingest events in (mostly) non-decreasing timestamp order and need fast range queries and retention.      | If you frequently insert heavily out-of-order, need random removals in the middle, or key lookups. |
| TemporalPriorityQueue<T>         | When priority-based ordering with timestamp tracking is required for dequeueing.                                   | If you only need FIFO or LIFO semantics without priorities.                                     |
| TemporalIntervalTree<T>          | When you need efficient interval overlap queries and interval-based time operations.                               | If your data are single points rather than intervals.                                           |
| TemporalDictionary<TKey, TValue> | When key-based access combined with timestamp tracking and querying is needed.                                     | If ordering or range queries by timestamp are required.                                         |
| TemporalMultimap<TKey, TValue>   | When each key can have multiple timestamped values and you need per-key queries and/or a global time-ordered view.| If you store a single value per key (use TemporalDictionary) or need ordering by non-time fields.|
| TemporalCircularBuffer<T>        | When you want a fixed-size buffer that overwrites oldest items with timestamp tracking.                            | If you need unbounded storage or complex queries.                                               |

## ITimeQueryable<T> Interface

All temporal collections implement the `ITimeQueryable<T>` interface, which provides a common set of methods to query and manage items based on their associated timestamps. This interface enables consistent time-based operations across different collection types.

### Key Methods

- **GetInRange(DateTimeOffset from, DateTimeOffset to)**  
  Returns an enumerable of temporal items whose timestamps fall within the inclusive range `[from, to]`. This allows filtering the collection by any desired time window.

- **RemoveOlderThan(DateTimeOffset cutoff)**  
  Removes all items with timestamps strictly older than the specified `cutoff` time (`Timestamp < cutoff`). This method is useful for pruning outdated data and maintaining collection size.

- **CountInRange(DateTimeOffset from, DateTimeOffset to)**  
  Returns the number of items with timestamps in the inclusive range `[from, to]`. Throws if to < from.

- **GetTimeSpan()**  
  Returns `latest.Timestamp - earliest.Timestamp`. Returns `TimeSpan.Zero` if the collection is empty or has a single item.

- **Clear()**  
  Removes all items from the collection.

- **RemoveRange(DateTimeOffset from, DateTimeOffset to)**  
  Removes all items with timestamps in the inclusive range `[from, to]`. Throws if `to < from`.

- **GetLatest()**  
  Returns the most recent item (max timestamp), or null if empty.

- **GetEarliest()**  
  Returns the oldest item (min timestamp), or null if empty.

- **GetBefore(DateTimeOffset time)**  
  Returns all items with `Timestamp < time` (strictly before), ordered by ascending timestamp.

- **GetAfter(DateTimeOffset time)**  
  Returns all items with `Timestamp > time` (strictly after), ordered by ascending timestamp.

- **CountSince(DateTimeOffset from)**  
  Counts the number of items with timestamp greater than or equal to the specified cutoff.

- **GetNearest(DateTimeOffset time)**  
  Retrieves the item whose timestamp is closest to the specified `time`.

These methods collectively support efficient and thread-safe temporal queries and cleanups, allowing each collection to manage its items according to their timestamps while exposing a unified API.

## 🚀 Getting Started with TemporalCollections
This section shows how to install and use **TemporalCollections** in your .NET projects with simple examples.

### Installation
```bash
dotnet add package TemporalCollections
```

### Basic usage
**TemporalQueue<T>**

```csharp
using System;
using System.Linq;
using TemporalCollections.Collections;

var queue = new TemporalQueue<string>();

// Enqueue items (timestamps are assigned automatically)
queue.Enqueue("event-1");
queue.Enqueue("event-2");

// Peek oldest (does not remove)
var oldest = queue.Peek();
Console.WriteLine($"Oldest: {oldest.Value} @ {oldest.Timestamp}");

// Dequeue oldest (removes)
var dequeued = queue.Dequeue();
Console.WriteLine($"Dequeued: {dequeued.Value} @ {dequeued.Timestamp}");

// Query by time range (inclusive)
var from = DateTime.UtcNow.AddMinutes(-5);
var to   = DateTime.UtcNow;
var inRange = queue.GetInRange(from, to);
foreach (var item in inRange)
{
    Console.WriteLine($"In range: {item.Value} @ {item.Timestamp}");
}
```
**TemporalSet<T>**

```csharp
using System;
using TemporalCollections.Collections;

var set = new TemporalSet<int>();

set.Add(1);
set.Add(2);
set.Add(2);

Console.WriteLine(set.Contains(1));

// Remove older than a cutoff
var cutoff = DateTime.UtcNow.AddMinutes(-10);
set.RemoveOlderThan(cutoff);

// Snapshot of all items ordered by timestamp
var items = set.GetItems();
```

**TemporalDictionary<TKey, TValue>**

```csharp
using System;
using System.Linq;
using TemporalCollections.Collections;

var dict = new TemporalDictionary<string, string>();

dict.Add("user:1", "login");
dict.Add("user:2", "logout");
dict.Add("user:1", "refresh");

// Range query across all keys
var from = DateTime.UtcNow.AddMinutes(-1);
var to   = DateTime.UtcNow.AddMinutes(1);
var all = dict.GetInRange(from, to);

// Range query for a specific key
var user1 = dict.GetInRange("user:1", from, to);

// Compute span covered by all events
var span = dict.GetTimeSpan();
Console.WriteLine($"Span: {span}");

// Remove a time window across all keys
dict.RemoveRange(from, to);
```

**TemporalStack<T>**

```csharp
using System;
using System.Linq;
using TemporalCollections.Collections;

var stack = new TemporalStack<string>();

// Push (timestamps assigned automatically, monotonic UTC)
stack.Push("first");
stack.Push("second");

// Peek last pushed (does not remove)
var top = stack.Peek();
Console.WriteLine($"Top: {top.Value} @ {top.Timestamp}");

// Pop last pushed (removes)
var popped = stack.Pop();
Console.WriteLine($"Popped: {popped.Value}");

// Time range query (inclusive)
var from = DateTime.UtcNow.AddMinutes(-5);
var to   = DateTime.UtcNow;
var items = stack.GetInRange(from, to).OrderBy(i => i.Timestamp);

// Remove older than cutoff
var cutoff = DateTime.UtcNow.AddMinutes(-10);
stack.RemoveOlderThan(cutoff);
```

**TemporalSlidingWindowSet<T>**

```csharp
using System;
using System.Linq;
using TemporalCollections.Collections;

var window = TimeSpan.FromMinutes(10);
var swSet = new TemporalSlidingWindowSet<string>(window);

// Add unique items (insertion timestamp recorded)
swSet.Add("A");
swSet.Add("B");

// Periodically expire items older than the window
swSet.RemoveExpired();

// Snapshot (ordered by timestamp)
var snapshot = swSet.GetItems().ToList();

// Query by time range
var from = DateTime.UtcNow.AddMinutes(-5);
var to   = DateTime.UtcNow;
var inRange = swSet.GetInRange(from, to);

// Manual cleanup by cutoff (if needed)
swSet.RemoveOlderThan(DateTime.UtcNow.AddMinutes(-30));
```

**TemporalSortedList<T>**

```csharp
using System;
using System.Linq;
using TemporalCollections.Collections;

var list = new TemporalSortedList<int>();

// Add items (kept sorted by timestamp internally)
list.Add(10);
list.Add(20);
list.Add(30);

// Fast range query via binary search (inclusive)
var from = DateTime.UtcNow.AddSeconds(-30);
var to   = DateTime.UtcNow;
var inRange = list.GetInRange(from, to);

// Before / After helpers
var before = list.GetBefore(DateTime.UtcNow);
var after  = list.GetAfter(DateTime.UtcNow.AddSeconds(-5));

// Housekeeping
list.RemoveOlderThan(DateTime.UtcNow.AddMinutes(-1));
Console.WriteLine($"Span: {list.GetTimeSpan()}");
```

**TemporalPriorityQueue<TPriority, TValue>**

```csharp
using System;
using System.Linq;
using TemporalCollections.Collections;

var pq = new TemporalPriorityQueue<int, string>();

// Enqueue with explicit priority (lower number = higher priority)
pq.Enqueue("high", priority: 1);
pq.Enqueue("low",  priority: 10);

// TryPeek (does not remove)
if (pq.TryPeek(out var next))
{
    Console.WriteLine($"Peek: {next}");
}

// TryDequeue (removes highest-priority; stable by insertion time)
while (pq.TryDequeue(out var val))
{
    Console.WriteLine($"Dequeued: {val}");
}

// Time-based queries are also available
var from = DateTime.UtcNow.AddMinutes(-5);
var to   = DateTime.UtcNow;
var items = pq.GetInRange(from, to);

Console.WriteLine($"Count in range: {pq.CountInRange(from, to)}");
```

**TemporalCircularBuffer<T>**

```csharp
using System;
using System.Linq;
using TemporalCollections.Collections;

// Fixed-capacity ring buffer; overwrites oldest when full
var buf = new TemporalCircularBuffer<string>(capacity: 3);

buf.Add("A");
buf.Add("B");
buf.Add("C");
buf.Add("D"); // Overwrites "A"

// Snapshot (oldest -> newest)
var snapshot = buf.GetSnapshot();
foreach (var it in snapshot)
{
    Console.WriteLine($"{it.Value} @ {it.Timestamp}");
}

// Range queries
var from = DateTime.UtcNow.AddMinutes(-5);
var to   = DateTime.UtcNow;
var inRange = buf.GetInRange(from, to);

// Remove a time window
buf.RemoveRange(from, to);

// Cleanup by cutoff (keeps >= cutoff)
buf.RemoveOlderThan(DateTime.UtcNow.AddMinutes(-1));
```

**TemporalIntervalTree<T>**

```csharp
using System;
using System.Linq;
using TemporalCollections.Collections;

var tree = new TemporalIntervalTree<string>();

var now = DateTime.UtcNow;
tree.Insert(now, now.AddMinutes(10), "session:A");
tree.Insert(now.AddMinutes(5), now.AddMinutes(15), "session:B");

// Overlap query (values only)
var overlapValues = tree.Query(now.AddMinutes(7), now.AddMinutes(12));
// Overlap query (with timestamps = interval starts)
var overlapItems  = tree.GetInRange(now.AddMinutes(7), now.AddMinutes(12));

Console.WriteLine($"Overlaps: {string.Join(", ", overlapValues)}");

// Remove intervals that ended before a cutoff
tree.RemoveOlderThan(now.AddMinutes(9));
```

### Common queries via `ITimeQueryable<T>`

```csharp
var latest   = collection.GetLatest();   // most recent item or null
var earliest = collection.GetEarliest(); // oldest item or null

var before = collection.GetBefore(DateTime.UtcNow); // strictly <
var after  = collection.GetAfter(DateTime.UtcNow);  // strictly >

var count = collection.CountInRange(DateTime.UtcNow.AddSeconds(-30), DateTime.UtcNow);

var span = collection.GetTimeSpan(); // latest.Timestamp - earliest.Timestamp (or TimeSpan.Zero)
```

## Monotonic Timestamp Guarantee
A key feature of the temporal collections is the guarantee that timestamps assigned to items are strictly monotonically increasing, even when multiple items are created concurrently or in rapid succession.

This is achieved through the `TemporalItem<T>` record, which uses an atomic compare-and-swap operation on a static internal timestamp counter. When creating a new temporal item, the current UTC timestamp in ticks (`DateTimeOffset.UtcTicks`) is retrieved and compared against the last assigned timestamp:

- If the current timestamp is greater than the last one, it is used as-is.
- If the current timestamp is less than or equal to the last assigned timestamp (e.g., due to rapid creation or clock precision limits), the timestamp is artificially incremented by one tick.

This approach ensures:

- **Uniqueness:** No two items share the exact same timestamp.
- **Strict ordering:** Timestamps always increase in time order.
- **Thread safety:** The mechanism works correctly across multiple threads without race conditions.

By enforcing this monotonic timestamp ordering, the temporal collections can rely on consistent time-based queries and maintain correct chronological order of items.

## 📈 Performance Benchmarks
We provide detailed performance measurements for all temporal data structures, including insertion, range queries, and removal operations.  
The full benchmark results are available here: [docs/benchmarks/benchmarks.md](docs/benchmarks/benchmarks.md)
These benchmarks help compare trade-offs between different collections and guide future optimizations.

## Threading Model & Big-O Cheatsheet

All collections are thread-safe. Locking granularity and common operations (amortized):

| Collection              | Locking                                   | Add/Push                               | Range Query          | RemoveOlderThan              |
|-------------------------|--------------------------------------------|-----------------------------------------|-----------------------|-------------------------------|
| TemporalQueue           | single lock around a queue snapshot        | O(1)                                    | O(n)                  | O(k) from head               |
| TemporalStack           | single lock; drain & rebuild for window ops| O(1)                                    | O(n)                  | O(n)                         |
| TemporalSet             | lock-free dict + per-bucket ops            | O(1) avg                                | O(n)                  | O(n)                         |
| TemporalSortedList      | single lock; binary search for ranges      | O(n) insert                             | **O(log n + m)**      | O(k)                         |
| TemporalSegmentedArray  | single lock; segmented storage             | O(1) amortized (in-order append)        | **O(log n + m)**      | O(n)                         |
| TemporalPriorityQueue   | single lock; SortedSet by (priority,time)  | O(log n)                                | O(n)                  | O(n)                         |
| TemporalIntervalTree    | single lock; interval overlap pruning      | O(log n) avg                            | **O(log n + m)**      | O(n)                         |
| TemporalDictionary      | concurrent dict + per-list lock            | O(1) avg                                | O(n)                  | O(n)                         |
| TemporalMultimap        | single lock; per-key ordered lists         | O(1) avg                                | O(n + m log m)        | O(n)                         |
| TemporalCircularBuffer  | single lock; ring overwrite                | O(1)                                    | O(n)                  | O(n)                         |

`n` = items, `m` = matches, `k` = removed.

## Notes
- **Deterministic ordering**: query results are returned in ascending timestamp order.
- **Snapshot semantics**: methods that return enumerables/lists provide a stable snapshot at call time.
- **Thread-safety**: all operations are designed to be thread-safe per collection.
- **Intervals**: for interval-based collections, the Timestamp used by this interface refers to the interval start.

⚠️ **Since v1.1.0, internal timestamp storage has been migrated from DateTime to DateTimeOffset (UTC). Public APIs remain DateTime for backward compatibility, but internal semantics are now strictly UTC-aware.**

### Contributing
Thank you for considering to help out with the source code!
If you'd like to contribute, please fork, fix, commit and send a pull request for the maintainers to review and merge into the main code base.

**Getting started with Git and GitHub**

 * [Setting up Git](https://docs.github.com/en/get-started/getting-started-with-git/set-up-git)
 * [Fork the repository](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/working-with-forks/fork-a-repo)
 * [Open an issue](https://github.com/engineering87/TemporalCollections/issues) if you encounter a bug or have a suggestion for improvements/features

## License
TemporalCollections source code is available under MIT License, see license in the source.

## Contact
Please contact at francesco.delre[at]protonmail.com for any details.
