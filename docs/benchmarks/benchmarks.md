# Performance Testing Summary

## Latest Run (Fast job)

### Environment
- **CPU**: Intel Core Ultra 7 155H, 22 logical / 16 physical cores
- **OS**: Windows 11 25H2
- **Runtime**: .NET 9.0.21, X64 RyuJIT x86-64-v3
- **BenchmarkDotNet**: v0.15.8, `Fast` job (`LaunchCount=1`, `WarmupCount=3`, `IterationCount=10`)
- **Dataset sizes**: 5,000 items per structure; `TemporalSegmentedArray` 20,000 items (`SegmentCapacity` = 256, 1024); `TemporalMultimap` 500 values per key (`KeyCount` = 10, 100)

> ⚠️ These numbers are **not directly comparable** with the 2025 results below (dataset size halved, different job and hardware).  
> Run `dotnet run -c Release -- --full` for the full BenchmarkDotNet default job.

All times are **mean in microseconds (µs)**.

### Core structures (5,000 items)

| Structure | AddItems (µs) | QueryRange (µs) | RemoveOlderThan (µs) |
|---|---:|---:|---:|
| TemporalSet | 271.5 | 0.30 | 0.08 |
| TemporalSlidingWindowSet | 292.9 | 0.32 | 0.09 |
| TemporalCircularBuffer | 287.5 | 0.08 | 0.04 |
| TemporalStack | 455.7 | 0.07 | 0.04 |
| TemporalSortedList | 477.6 | 0.07 | 0.04 |
| TemporalIntervalTree | 530.2 | 0.07 | 0.04 |
| TemporalDictionary | 611.7 | 0.11 | 0.08 |
| TemporalPriorityQueue | 5,085.7 | 0.14 | 0.06 |

**TemporalQueue**: `Enqueue` 0.08 µs, `Dequeue` 0.07 µs, `Peek` 0.02 µs, `RemoveOlderThan` 0.01 µs, `GetInRange_Count` 20.5 µs.

### TemporalCache (5,000 items)

| Method | Mean (µs) | Allocated |
|---|---:|---:|
| RemoveExpired scan | 47.0 | 64 B |
| TryGetValue (absolute) | 128.7 | 0 B |
| GetOrAdd | 134.7 | 0 B |
| QueryRange (last 10 minutes) | 196.4 | 131,584 B |
| TryGetValue (sliding) | 265.5 | 200,000 B |
| Set | 484.7 | 440,000 B |

### TemporalSegmentedArray (20,000 items, range across `SegmentCapacity` 256 / 1024)

| Method | Mean (µs) |
|---|---:|
| Clear / Extremes / CountInRange / GetNearest / RemoveRange | 0.8 – 4.5 |
| TrimExcess / ToArray | 11 – 16 |
| GetBefore / GetAfter (midpoint) | 51 – 54 |
| AddSorted (sorted input) | 129 – 134 |
| Add pre-built (sorted) | 441 – 480 |
| AddValue | 1,496 – 1,546 |
| AddRange / Add pre-built (**unsorted** input) | 4,825 – 5,133 |

> Unsorted input is **~35× slower** than `AddSorted`: prefer sorted batches when possible.

### TemporalMultimap (indicative only)

Results show high variance (up to ±60% StdDev on global queries) because of per-iteration setup; they are **not reported as reference values**.  
Order of magnitude: per-key removals/queries 1–30 µs, global counts/removals 4–65 µs, inserts 0.2–5 ms, global range/nearest queries 0.4–11 ms.

### Takeaways
- `QueryRange` and `RemoveOlderThan` remain **sub-microsecond** across all core structures.
- `TemporalPriorityQueue` is still the most expensive on inserts (~10× the other structures).
- `TemporalCache` sliding expiration costs ~2× absolute expiration and allocates on every read.
- Extrapolating to 10,000 items (linear scaling, indicative only), `TemporalDictionary`, `TemporalIntervalTree` and `TemporalSortedList` inserts appear faster than in the 2025 run.

---

# Historical Results (2025-08-19)

### Overview
The following benchmarks summarize the performance of different temporal data structures across core operations: **AddItems**, **QueryRange**, and **RemoveOlderThan**.  
Measurements are given in **milliseconds (ms)** with mean, standard deviation, and throughput (**Ops/sec**).

> **What's New**  
> In this release we have **updated and optimized several algorithms** across temporal data structures (insertions, interval handling, cleanup).  
> The main goal was to reduce the average cost of `AddItems`, stabilize queries, and eliminate the critical bottlenecks observed in the first benchmark.

## Performance Summary

- ✅ **Major fix for `TemporalIntervalTree`**: `AddItems` improved from ~**2443.45 ms** to **1.81 ms** (from ~0 to **553 ops/sec**).  
- ✅ **Widespread improvements** on `AddItems` across most structures (−2% to −3% average).  
- ⚠️ **Slight regression** on `TemporalDictionary.AddItems` (+8% in mean time).  
- ➖ `QueryRange` and `RemoveOlderThan` remain **extremely fast and stable** (minimal variance).

> Note: All numbers are in **ms** (mean), with **Ops/sec** reflecting improvements when mean time decreases.

---

## Before → After (Δ %)

| Structure | Method | Mean ms (before → after) | Ops/sec (before → after) | Δ Mean |
|---|---|---:|---:|---:|
| **TemporalSet** | AddItems | 0.5792 → **0.5662** | 1,727 → **1,766** | **−2.3%** |
| **TemporalDictionary** | AddItems | 1.6731 → **1.8061** | 598 → **554** | **+7.9%** |
| **TemporalIntervalTree** | AddItems | 2443.4536 → **1.8073** | ~0 → **553** | **−99.93%** |
| **TemporalPriorityQueue** | AddItems | 12.6041 → **12.2112** | 79 → **82** | **−3.1%** |
| **TemporalSortedList** | AddItems | 2.2646 → **2.1936** | 442 → **456** | **−3.1%** |
| **TemporalQueue** | GetInRange_Count | 0.0549 → **0.0543** | 18,206 → **18,410** | **−1.1%** |
| **TemporalStack** | AddItems | 0.9937 → **0.9794** | 1,006 → **1,021** | **−1.4%** |
| **TemporalSlidingWindowSet** | AddItems | 0.5766 → **0.5573** | 1,734 → **1,794** | **−3.3%** |
| **TemporalCircularBuffer** | AddItems | 0.5191 → **0.5165** | 1,926 → **1,936** | **−0.5%** |

### Stable Methods (same order of magnitude)
- `RemoveOlderThan` on all structures: still **~0.0000–0.0001 ms** (tens of millions ops/sec).  
- `QueryRange` on set/list/tree: still **~0.0001–0.0003 ms** (≈ 3.4–15.3 M ops/sec).  
- **TemporalQueue basics** (`Peek`, `Dequeue`, `Enqueue`): unchanged, always **O(1)** with **10–38 M ops/sec**.

---

## Highlights per Structure

- **TemporalIntervalTree**
  - **Critical fix for insertions**: `AddItems` now competitive (≈ **1.81 ms**, **~553 ops/sec**) instead of unusable.
- **TemporalSet / TemporalSlidingWindowSet / TemporalSortedList / TemporalStack / TemporalCircularBuffer / TemporalPriorityQueue**
  - **Faster insertions** (−1% to −3%), with no regressions in queries or cleanup.
- **TemporalDictionary**
  - **Slightly slower inserts** (+8% mean): worth investigating further (hash collisions, memory allocations, resizing, etc.).
