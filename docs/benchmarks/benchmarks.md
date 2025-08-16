# Performance Testing Summary

### Overview
The following benchmarks summarize the performance of different temporal data structures across core operations: **AddItems**, **QueryRange**, and **RemoveOlderThan**.  
Measurements are given in **milliseconds (ms)** with mean, standard deviation, and throughput (**Ops/sec**).

---

## Results Table

| Structure                        | Method          | Mean (ms) | StdDev (ms) | Min (ms) | Max (ms) | Ops/sec   |
|----------------------------------|-----------------|-----------|-------------|----------|----------|-----------|
| TemporalSet                      | RemoveOlderThan | 0.0001    | 0.0000      | 0.0001   | 0.0001   | 10,088,315 |
|                                  | QueryRange      | 0.0003    | 0.0000      | 0.0003   | 0.0003   | 3,485,457 |
|                                  | AddItems        | 0.5792    | 0.0128      | 0.5647   | 0.6089   | 1,727     |
| TemporalDictionary               | RemoveOlderThan | 0.0001    | 0.0000      | 0.0001   | 0.0001   | 9,367,346 |
|                                  | QueryRange      | 0.0001    | 0.0000      | 0.0001   | 0.0001   | 7,432,042 |
|                                  | AddItems        | 1.6731    | 0.1748      | 1.3152   | 2.1704   | 598       |
| TemporalIntervalTree             | RemoveOlderThan | 0.0000    | 0.0000      | 0.0000   | 0.0000   | 24,948,475 |
|                                  | QueryRange      | 0.0001    | 0.0000      | 0.0001   | 0.0001   | 13,605,101 |
|                                  | AddItems        | 2443.4536 | 53.5131     | 2370.1983| 2551.2699| ~0        |
| TemporalPriorityQueue            | RemoveOlderThan | 0.0001    | 0.0000      | 0.0001   | 0.0001   | 16,547,903 |
|                                  | QueryRange      | 0.0002    | 0.0000      | 0.0002   | 0.0002   | 5,643,250 |
|                                  | AddItems        | 12.6041   | 1.3750      | 9.1438   | 15.4882  | 79        |
| TemporalSortedList               | RemoveOlderThan | 0.0000    | 0.0000      | 0.0000   | 0.0000   | 25,098,313 |
|                                  | QueryRange      | 0.0001    | 0.0000      | 0.0001   | 0.0001   | 14,948,315 |
|                                  | AddItems        | 2.2646    | 0.0678      | 2.1107   | 2.3872   | 442       |
| TemporalQueue                    | RemoveOlderThan | 0.0000    | 0.0000      | 0.0000   | 0.0000   | 52,303,999 |
|                                  | Peek            | 0.0000    | 0.0000      | 0.0000   | 0.0000   | 37,989,558 |
|                                  | Dequeue         | 0.0001    | 0.0000      | 0.0001   | 0.0001   | 12,602,093 |
|                                  | Enqueue         | 0.0001    | 0.0000      | 0.0001   | 0.0001   | 10,098,160 |
|                                  | GetInRange_Count| 0.0549    | 0.0010      | 0.0533   | 0.0568   | 18,206    |
| TemporalStack                    | RemoveOlderThan | 0.0000    | 0.0000      | 0.0000   | 0.0000   | 23,321,482 |
|                                  | QueryRange      | 0.0001    | 0.0000      | 0.0001   | 0.0001   | 13,229,459 |
|                                  | AddItems        | 0.9937    | 0.0111      | 0.9798   | 1.0101   | 1,006     |
| TemporalSlidingWindowSet         | RemoveOlderThan | 0.0001    | 0.0000      | 0.0001   | 0.0001   | 10,050,340 |
|                                  | QueryRange      | 0.0003    | 0.0000      | 0.0003   | 0.0003   | 3,543,217 |
|                                  | AddItems        | 0.5766    | 0.0068      | 0.5665   | 0.5884   | 1,734     |
| TemporalCircularBuffer           | RemoveOlderThan | 0.0000    | 0.0000      | 0.0000   | 0.0000   | 23,390,374 |
|                                  | QueryRange      | 0.0001    | 0.0000      | 0.0001   | 0.0001   | 13,456,901 |
|                                  | AddItems        | 0.5191    | 0.0065      | 0.5105   | 0.5331   | 1,926     |

---

## Observations

- **Insertion Performance (AddItems)**  
  - Fastest: **TemporalCircularBuffer** (0.52 ms, ~1926 ops/sec).  
  - Slowest: **TemporalIntervalTree** (~2443 ms, effectively unusable for bulk inserts).  
  - Middle ground: **TemporalSet** and **SlidingWindowSet** (~0.57 ms), **Stack** (~0.99 ms).

- **Query Performance (QueryRange)**  
  - All structures (except queue's `GetInRange_Count`) perform in **0.0001–0.0003 ms** range with multi-million ops/sec.  
  - Best: **TemporalSortedList** (~14.9M ops/sec), **IntervalTree** (~13.6M ops/sec).  
  - Queue’s `GetInRange_Count` is much slower (~0.055 ms, ~18K ops/sec).

- **RemoveOlderThan Performance**  
  - Extremely fast across all structures, typically **0.0000–0.0001 ms** with tens of millions ops/sec.  
  - Best: **TemporalQueue** (~52M ops/sec).

- **Special Cases**  
  - **TemporalQueue** shines in throughput for core ops (Enqueue/Dequeue/Peek all ~10M–38M ops/sec).  
  - **TemporalIntervalTree** is extremely efficient for queries but suffers catastrophic insertion cost.  
  - **TemporalSortedList** balances query performance and acceptable insert times (~2.26 ms).

---

## Conclusion

- For **high-throughput insertion + queries**, use **TemporalCircularBuffer** or **SlidingWindowSet**.  
- For **extremely fast queries** on preloaded data, **TemporalSortedList** or **IntervalTree** excel.  
- **TemporalQueue** offers the best **general throughput** across enqueue/dequeue scenarios.  
- Avoid **TemporalIntervalTree** for frequent insertions, its insertion time dominates all others.

## Future Work

While most structures show excellent performance, the **TemporalIntervalTree** clearly suffers from very high insertion costs, making it impractical for workloads with frequent updates.  
Future optimization efforts will focus on:

- Improving **insertion efficiency** for the interval tree (e.g., alternative balancing strategies or lazy insertions).  
- Exploring **hybrid data structures** that maintain fast queries without penalizing bulk inserts.  
- Reducing memory overhead for frequently updated collections.  