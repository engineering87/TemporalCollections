# Performance Testing Summary (Updated – 2025-08-19)

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
