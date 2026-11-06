# Sampling and buffering performance tests

## Goal

Measure the incremental CPU and managed-memory cost of log sampling and buffering relative to the same logging pipeline without either feature. The sampling comparison must also show when dropping logs offsets the sampler's own cost.

BenchmarkDotNet mean time per operation is used as the CPU-cost proxy. `MemoryDiagnoser` reports managed allocations and garbage collections per operation. These measurements intentionally do not represent process working set or machine-wide CPU utilization.

Each benchmark invocation processes either 10,000 or 20,000 logs, representing one minute of traffic at the selected rate. BenchmarkDotNet reports time and allocation for the complete one-minute batch. Divide those values by `RecordsPerMinute` when a per-log value is needed.

The shared deterministic workload uses four categories and sixteen event IDs. Event frequency is intentionally skewed: event 1 accounts for 70% of records, event 2 for 15%, event 3 for 8%, and the remaining 7% is spread across events 4 through 16. This exercises category/event rule caches and gives bottom-K sampling both frequent and rare callsites.

.NET sampling benchmarks invoke each method eight times per iteration. Combined with the configured ten warmup iterations, each sampling benchmark process executes 80 warmup batches before measurement. Buffering and bottom-K sampling use one invocation per iteration so `IterationCleanup` empties retained state after every batch and each invocation continues to represent one minute of traffic.

Benchmark worker processes disable tiered compilation with `DOTNET_TieredCompilation=0`. This makes every launch use optimized JIT-generated code from the start, avoiding tier-promotion differences between launches while retaining BenchmarkDotNet's ten warmup iterations.

## Retained-memory measurement

`--retained-memory` runs each pipeline outside BenchmarkDotNet, fills it with both record counts, and reports:

- `Retained managed`: the change in live managed memory after a forced full collection.
- `Peak managed`: the largest observed managed heap increase while filling the pipeline, including objects that may later be collected.
- `Peak working set`: the largest observed increase in physical process memory.
- `Peak private bytes`: the largest observed increase in committed private process memory.

Memory is sampled every 256 records. Managed retained memory is the most useful comparison for buffers and sampler caches. Working-set and private-byte deltas are process-level measurements and can be affected by runtime heap reservation, operating-system paging, and earlier scenarios in the same process.

The benchmarks process the minute's traffic as a batch rather than sleeping between logs. This keeps wall-clock waiting out of the measurements and isolates logging pipeline cost.

## Sampling benchmark

`SamplingImpactBench` builds two otherwise identical logging pipelines:

- `NoSampling` is the baseline and sends every log to `BenchLogger`.
- `WithSampling` adds one sampler before the same provider.

Both methods use a cached `LoggerMessage` delegate with one primitive structured argument. This avoids call-site formatting and boxing allocations while still exercising the structured log state that passes through the sampling pipeline, making sampler-introduced allocations visible.

The benchmark runs the following scenarios:

| Scenario | Configuration | Purpose |
| --- | --- | --- |
| `RandomSampleAll` | Probability `1.0` | Measures random sampler overhead when provider work is unchanged. |
| `RandomSampleOnePercent` | Probability `0.01` | Represents a high-volume production configuration where most logs are discarded. |
| `RandomDropAll` | Probability `0.0` | Establishes the lower bound when all provider work is avoided. |
| `RandomByCategory` | High-volume categories `0.01`, critical categories `1.0`, fallback `0.1` | Measures category-rule selection with different retention policies. |
| `TraceSample` | Current activity has the `Recorded` flag | Measures trace-based sampler overhead when the log is retained. |
| `TraceDrop` | Current activity is not recorded | Measures trace-based sampling when provider work is avoided. |

Each scenario is a separate BenchmarkDotNet parameter, so its `WithSampling` result is compared directly with a `NoSampling` baseline created under the same process and job settings. Setup, dependency injection, logger creation, and activity creation are outside the measured operations.

## Buffering benchmark

`BufferingImpactBench` builds two otherwise identical logging pipelines:

- `NoBuffering` is the baseline and sends the selected record count directly to `BenchLogger`.
- `BufferOnly` measures serialization and insertion of the selected record count into the global buffer. Its flush runs during iteration cleanup and is excluded from the measurement.
- `BufferAndFlush` measures the end-to-end cost of buffering and then emitting the selected record count to `BenchLogger`.

The buffer is sized to retain all batches from a measured iteration and automatic post-flush bypass is disabled. `BufferOnly` is flushed after each benchmark iteration, outside the measurement. This avoids capacity eviction and ensures every measured iteration starts with an empty buffer.

## Bottom-K benchmark

`BottomKImpactBench` measures the bottom-K integration. Bottom-K combines the sampling decision and reservoir buffering in one pipeline, so its decision and buffer costs cannot be isolated through the public logging integration.

- `NoSampling` is the baseline and sends every log directly to `BenchLogger`.
- `BottomKRetainAll` gives the reservoir enough capacity for the selected record count and measures admission plus buffering with no drops.
- `BottomKRetainAllAndFlush` adds emission of every retained log, making downstream provider work equivalent to the baseline.
- `BottomKDisabled` measures the options-monitor and policy-check overhead of a registered bottom-K pipeline with its kill switch disabled.
- `BottomKRetainAllPolicy` measures a registered Bottom-K pipeline with no matching sampling rules.
- `BottomKAdaptive` uses a representative fixed capacity of 128 per category (up to 512 records across the four-category workload) for the selected one-minute period and measures the adaptive high-volume path without flush cost.
- `BottomKAdaptiveAndFlush` includes emission of the adaptive reservoir at the period boundary.

The novelty preserve is disabled so retained records are controlled only by the configured reservoir capacity. Automatic time-based flushing is moved beyond the benchmark duration, and iteration cleanup flushes both reservoirs outside the measurement. Bottom-K uses random ranks, so adaptive results should be interpreted from the full BenchmarkDotNet run rather than a single invocation.

### Bottom-K log-level policy cost

`BottomKLogLevelPolicyBench` isolates the incremental cost of a log-level-constrained sampling rule. Both pipelines process the same Information-only workload with the same bottom-K capacity, preserve setting, flush interval, categories, event IDs, and exporter. The baseline uses an unrestricted rule. The comparison uses a rule through `Warning`, which still matches, so both pipelines perform equivalent bottom-K admission and retain equivalent output.

The benchmark measures the full logging path. `LogLevel` is already present on `LogEntry`; reading it does not construct or parse a value. The reported delta therefore covers evaluating the configured log-level constraint.

### Bottom-K category policy cost

`BottomKCategoryPolicyBench` isolates the incremental cost of checking category sampling rules. All pipelines use the same bottom-K configuration and workload. The comparisons check either two or 100 non-matching wildcard patterns, or 100 non-matching exact categories, followed by a catch-all sampling rule, so every record continues through the same adaptive bottom-K path as the zero-policy baseline. Rules are evaluated by the shared sampling-rule selector; rerun this benchmark after changes to rule selection.

### Bottom-K EventId policy cost

`BottomKEventIdPolicyBench` isolates the incremental cost of checking EventId sampling rules. The comparison checks two non-matching identifiers, `10001` and `10002`, before a catch-all rule. The workload uses EventIds 1 through 16, so every record continues through the same adaptive bottom-K path as the baseline.

## Serialized-exporter benchmark

`SerializedExporterImpactBench` is independent from the sampler and buffering microbenchmarks. It uses a dedicated provider that formats retained messages and serializes their category, level, EventId, event name, formatted message, exception, and structured attributes to UTF-8 JSON in a reusable buffer. It performs no terminal, disk, or network I/O.

Each strategy is compared with a no-sampling pipeline using the same serialized exporter:

- `RandomOnePercent` immediately exports approximately 1% of records.
- `RandomByCategory` applies the same 1% high-volume, 100% critical, and 10% fallback rules as the sampling microbenchmark.
- `TraceRetain` exports every record from a recorded activity.
- `TraceDrop` drops every record from an unrecorded activity.
- `BottomKOnePercent` uses a per-category reservoir sized to retain approximately 1% across all four categories and includes the required flush and weighted-record serialization in the measured operation.

This suite measures whether avoided formatting and serialization offset sampling overhead. The exporter retains the last serialized payload in its reusable output buffer, preserving all serialization work while keeping external I/O noise out of the results.

`--serialized-exporter-volume` runs the same pipelines with exporter counters enabled and reports the actual number of records and UTF-8 bytes emitted. Counters are disabled in the timed benchmark so metric collection does not affect performance results.

## Category-cardinality benchmark

`CategoryCardinalityImpactBench` is an independent serialized-exporter suite for 20,000 records and 50, 100, or 200 categories. Both random sampling and bottom-K use a 1% output budget. At 20,000 records each category receives enough traffic for an integral bottom-K capacity of 4, 2, or 1 respectively. This measures rule-cache, logger, per-category reservoir, flush, and serialization scaling as category cardinality grows.

## Multithreaded contention benchmark

`MultithreadedSamplingImpactBench` processes 20,000 records across 100 shared categories using 1, 4, or 8 workers. Worker record indices are interleaved so workers concurrently target the same category instead of operating on disjoint category ranges. It uses the lightweight provider to emphasize sampler synchronization and compares random 1% with bottom-K 1%, including the bottom-K flush.

## Sustained GC-pressure measurement

`--sustained-gc` compares random 1% sampling with bottom-K under a production-shaped, two-minute run at 10,000 logs per minute. Each strategy runs in an isolated worker process after pipeline warmup and a forced-GC baseline. Bottom-K uses its configurable 30-second automatic flush interval, producing four sampling periods, and both strategies use the serialized exporter with output counters enabled.

An optional positive integer argument overrides the logging rate while retaining the two-minute duration and 30-second flush interval. A second optional argument overrides the bottom-K flush interval in seconds. This supports higher-volume stress runs that generate enough allocation traffic to observe collections and compares the effect of record lifetime:

```powershell
dotnet run -c Release --project .\bench\Libraries\Microsoft.Extensions.Telemetry.PerformanceTests\Microsoft.Extensions.Telemetry.PerformanceTests.csproj -- --sustained-gc 1000000 1
```

The diagnostic reports input and emitted volume, exporter batch callbacks, total allocated bytes, Gen0/Gen1/Gen2 collection counts, cumulative GC pause time, process CPU time, CPU time as a percentage of wall time, peak managed-memory growth, and retained managed memory after a final forced collection. The final forced collection is performed after the collection counts, pause time, and CPU time are captured, so it does not contribute to those pressure measurements. Process working-set polling is intentionally left to the separate retained-memory diagnostic because repeatedly refreshing operating-system process counters would distort CPU measurements at this logging rate.

At four categories, an exact 1% bottom-K budget would require 12.5 records per category per 30-second period. The integer capacity is therefore 12 records per category, or 48 of each 5,000-record period (0.96%). Actual emitted volume is reported beside random sampling's probabilistic output.

## Running

From the repository root:

```powershell
dotnet run -c Release --project .\bench\Libraries\Microsoft.Extensions.Telemetry.PerformanceTests\Microsoft.Extensions.Telemetry.PerformanceTests.csproj -- --filter *SamplingImpactBench* *BufferingImpactBench* *BottomKImpactBench*
```

Run the independent serialized-exporter comparison:

```powershell
dotnet run -c Release --project .\bench\Libraries\Microsoft.Extensions.Telemetry.PerformanceTests\Microsoft.Extensions.Telemetry.PerformanceTests.csproj -- --filter *SerializedExporterImpactBench*
```

Measure the incremental bottom-K log-level policy cost:

```powershell
dotnet run -c Release --project .\bench\Libraries\Microsoft.Extensions.Telemetry.PerformanceTests\Microsoft.Extensions.Telemetry.PerformanceTests.csproj -- --filter *BottomKLogLevelPolicyBench*
```

Measure category and EventId sampling-rule costs independently:

```powershell
dotnet run -c Release --project .\bench\Libraries\Microsoft.Extensions.Telemetry.PerformanceTests\Microsoft.Extensions.Telemetry.PerformanceTests.csproj -- --filter *BottomKCategoryPolicyBench* *BottomKEventIdPolicyBench*
```

Validate actual serialized output volume:

```powershell
dotnet run -c Release --project .\bench\Libraries\Microsoft.Extensions.Telemetry.PerformanceTests\Microsoft.Extensions.Telemetry.PerformanceTests.csproj -- --serialized-exporter-volume
```

Run category-cardinality and multithreaded contention benchmarks:

```powershell
dotnet run -c Release --project .\bench\Libraries\Microsoft.Extensions.Telemetry.PerformanceTests\Microsoft.Extensions.Telemetry.PerformanceTests.csproj -- --filter *CategoryCardinalityImpactBench* *MultithreadedSamplingImpactBench*
```

Measure retained and peak memory separately:

```powershell
dotnet run -c Release --project .\bench\Libraries\Microsoft.Extensions.Telemetry.PerformanceTests\Microsoft.Extensions.Telemetry.PerformanceTests.csproj -- --retained-memory
```

Run the isolated two-minute-per-strategy sustained GC-pressure comparison:

```powershell
dotnet run -c Release --project .\bench\Libraries\Microsoft.Extensions.Telemetry.PerformanceTests\Microsoft.Extensions.Telemetry.PerformanceTests.csproj -- --sustained-gc
```

Run on an otherwise idle machine with a fixed power plan. Compare `Mean`, `Ratio`, `Allocated`, and GC columns. Retain the generated BenchmarkDotNet artifacts with the machine, OS, runtime, and processor metadata when comparing changes over time.

## Interpretation

- `RandomSampleAll` and `TraceSample` isolate the cost of making a sampling decision because both paths still invoke the provider.
- `RandomDropAll` and `TraceDrop` show the best-case savings when sampling bypasses provider processing.
- `RandomSampleOnePercent` captures the combined decision cost and expected provider savings, but individual invocations are nondeterministic. BenchmarkDotNet's repeated operations provide the aggregate result.
- `RandomByCategory` retains approximately 28% of this evenly distributed four-category workload: 1% for two high-volume categories, 100% for the critical category, and 10% for the fallback category.
- Results depend on provider cost. `BenchLogger` is deliberately lightweight, so dropped-path savings are conservative relative to providers that format, serialize, buffer, or export logs.
- `BufferOnly` isolates the cost and managed allocations required to retain logs in memory.
- `BufferAndFlush` includes deserialization and downstream provider work, so it represents the complete buffering lifecycle.
- `BottomKRetainAll` shows the combined admission and buffering overhead when sampling provides no volume reduction.
- `BottomKAdaptive` shows when dropped-log savings offset reservoir decision cost, while the `AndFlush` variants include the cost of emitting retained records.

## Follow-up plan

1. Record a baseline on each supported performance-test platform.
2. Track accepted-path CPU and allocation regressions separately from dropped-path throughput gains.
3. Add a representative production exporter benchmark only if end-to-end exporter savings are needed; keep it separate so I/O and serialization do not hide sampler regressions.
4. Add multithreaded contention coverage if rule-cache or random-number generation changes, because this initial benchmark isolates steady-state single-thread overhead.

## Acceptance criteria

- Every sampling implementation has both a retained and discarded-log scenario.
- Each sampling result has an equivalent no-sampling baseline.
- Buffer insertion and buffer insertion plus flush are both compared with direct logging.
- Bottom-K covers retain-all and adaptive-drop paths, both before and through flush.
- Every measured invocation represents either 10,000 or 20,000 logs and reports the complete one-minute batch cost.
- Reports include time per operation, ratio, managed allocation per operation, and GC counts.
- Benchmark setup and the post-iteration `BufferOnly` flush do not contribute to measured CPU or allocation results.
