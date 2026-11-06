# Benchmarks

## Initial benchmark

BenchmarkDotNet=v0.13.5, OS=Windows 11 (10.0.22621.1848/22H2/2022Update/SunValley2)
Intel Core i9-10900K CPU 3.70GHz, 1 CPU, 20 logical and 10 physical cores
.NET SDK=8.0.100-preview.7.23328.2
  [Host] : .NET 8.0.0 (8.0.23.32605), X64 RyuJIT AVX2

Job=MediumRun  Toolchain=InProcessEmitToolchain  IterationCount=15
LaunchCount=2  WarmupCount=10

|                         Method |          Factory |      Mean |    Error |   StdDev |    Median |   Gen0 | Allocated |
|------------------------------- |----------------- |----------:|---------:|---------:|----------:|-------:|----------:|
|               Classic_RefTypes |         Original |  86.70 ns | 2.114 ns | 3.098 ns |  87.53 ns | 0.0106 |     112 B |
|             Classic_ValueTypes |         Original |  95.07 ns | 1.104 ns | 1.547 ns |  96.09 ns | 0.0283 |     296 B |
|   LoggerMessageDefine_RefTypes |         Original |  80.07 ns | 1.320 ns | 1.851 ns |  80.20 ns | 0.0068 |      72 B |
| LoggerMessageDefine_ValueTypes |         Original |  84.22 ns | 2.054 ns | 3.011 ns |  83.69 ns | 0.0252 |     264 B |
|        ClassicCodeGen_RefTypes |         Original |  80.36 ns | 0.517 ns | 0.690 ns |  80.39 ns | 0.0068 |      72 B |
|      ClassicCodeGen_ValueTypes |         Original |  82.33 ns | 1.197 ns | 1.678 ns |  81.97 ns | 0.0252 |     264 B |
|         ModernCodeGen_RefTypes |         Original |  59.31 ns | 0.379 ns | 0.556 ns |  59.09 ns |      - |         - |
|       ModernCodeGen_ValueTypes |         Original |  78.59 ns | 1.769 ns | 2.594 ns |  78.45 ns | 0.0160 |     168 B |
|               Classic_RefTypes |              New | 138.53 ns | 0.826 ns | 1.184 ns | 138.32 ns | 0.0143 |     152 B |
|             Classic_ValueTypes |              New | 141.50 ns | 0.658 ns | 0.984 ns | 141.33 ns | 0.0319 |     336 B |
|   LoggerMessageDefine_RefTypes |              New | 149.70 ns | 0.953 ns | 1.398 ns | 149.51 ns | 0.0136 |     144 B |
| LoggerMessageDefine_ValueTypes |              New | 131.45 ns | 0.656 ns | 0.920 ns | 131.74 ns | 0.0312 |     328 B |
|        ClassicCodeGen_RefTypes |              New | 146.57 ns | 3.755 ns | 5.139 ns | 148.38 ns | 0.0136 |     144 B |
|      ClassicCodeGen_ValueTypes |              New | 129.45 ns | 0.772 ns | 1.108 ns | 129.99 ns | 0.0312 |     328 B |
|         ModernCodeGen_RefTypes |              New |  60.68 ns | 0.633 ns | 0.887 ns |  60.51 ns |      - |         - |
|       ModernCodeGen_ValueTypes |              New |  80.78 ns | 0.373 ns | 0.536 ns |  80.61 ns | 0.0160 |     168 B |
|               Classic_RefTypes | NewWithEnrichers | 144.45 ns | 0.556 ns | 0.798 ns | 144.60 ns | 0.0143 |     152 B |
|             Classic_ValueTypes | NewWithEnrichers | 148.00 ns | 0.766 ns | 1.049 ns | 147.48 ns | 0.0319 |     336 B |
|   LoggerMessageDefine_RefTypes | NewWithEnrichers | 148.39 ns | 1.016 ns | 1.457 ns | 147.84 ns | 0.0136 |     144 B |
| LoggerMessageDefine_ValueTypes | NewWithEnrichers | 137.76 ns | 0.582 ns | 0.835 ns | 137.66 ns | 0.0312 |     328 B |
|        ClassicCodeGen_RefTypes | NewWithEnrichers | 147.13 ns | 2.168 ns | 3.178 ns | 148.46 ns | 0.0136 |     144 B |
|      ClassicCodeGen_ValueTypes | NewWithEnrichers | 135.49 ns | 0.965 ns | 1.414 ns | 134.96 ns | 0.0312 |     328 B |
|         ModernCodeGen_RefTypes | NewWithEnrichers |  67.11 ns | 0.379 ns | 0.556 ns |  66.95 ns |      - |         - |
|       ModernCodeGen_ValueTypes | NewWithEnrichers |  85.60 ns | 0.710 ns | 0.972 ns |  86.24 ns | 0.0160 |     168 B |

## 2025 update with log sampling


BenchmarkDotNet=v0.13.5, OS=macOS 15.5 (24F5053j) [Darwin 24.5.0]
Apple M3 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK=9.0.202
[Host]    : .NET 9.0.3 (9.0.325.11113), Arm64 RyuJIT AdvSIMD
MediumRun : .NET 9.0.3 (9.0.325.11113), Arm64 RyuJIT AdvSIMD

Job=MediumRun  IterationCount=15  LaunchCount=2  
WarmupCount=10

|                         Method |              Factory |      Mean |    Error |   StdDev |    Median |   Gen0 | Allocated |
|------------------------------- |--------------------- |----------:|---------:|---------:|----------:|-------:|----------:|
|               Classic_RefTypes |             Original |  54.63 ns | 0.836 ns | 1.198 ns |  55.64 ns | 0.0086 |      72 B |
|             Classic_ValueTypes |             Original |  57.51 ns | 0.156 ns | 0.219 ns |  57.55 ns | 0.0306 |     256 B |
|   LoggerMessageDefine_RefTypes |             Original |  40.60 ns | 0.178 ns | 0.266 ns |  40.64 ns | 0.0086 |      72 B |
| LoggerMessageDefine_ValueTypes |             Original |  50.24 ns | 0.538 ns | 0.788 ns |  50.91 ns | 0.0315 |     264 B |
|        ClassicCodeGen_RefTypes |             Original |  40.31 ns | 0.026 ns | 0.039 ns |  40.31 ns | 0.0086 |      72 B |
|      ClassicCodeGen_ValueTypes |             Original |  49.61 ns | 0.272 ns | 0.381 ns |  49.50 ns | 0.0315 |     264 B |
|         ModernCodeGen_RefTypes |             Original |  32.44 ns | 0.257 ns | 0.385 ns |  32.37 ns |      - |         - |
|       ModernCodeGen_ValueTypes |             Original |  44.73 ns | 1.077 ns | 1.579 ns |  46.13 ns | 0.0200 |     168 B |
|               Classic_RefTypes | OriginalWithSampling | 185.05 ns | 0.278 ns | 0.398 ns | 185.07 ns | 0.0219 |     184 B |
|             Classic_ValueTypes | OriginalWithSampling | 195.47 ns | 0.671 ns | 0.984 ns | 195.29 ns | 0.0439 |     368 B |
|   LoggerMessageDefine_RefTypes | OriginalWithSampling | 173.63 ns | 0.630 ns | 0.924 ns | 173.72 ns | 0.0210 |     176 B |
| LoggerMessageDefine_ValueTypes | OriginalWithSampling | 174.71 ns | 0.214 ns | 0.301 ns | 174.67 ns | 0.0429 |     360 B |
|        ClassicCodeGen_RefTypes | OriginalWithSampling | 174.12 ns | 0.373 ns | 0.547 ns | 174.23 ns | 0.0210 |     176 B |
|      ClassicCodeGen_ValueTypes | OriginalWithSampling | 172.95 ns | 0.594 ns | 0.871 ns | 172.77 ns | 0.0429 |     360 B |
|         ModernCodeGen_RefTypes | OriginalWithSampling | 149.22 ns | 0.658 ns | 0.985 ns | 149.44 ns | 0.0038 |      32 B |
|       ModernCodeGen_ValueTypes | OriginalWithSampling | 163.12 ns | 0.996 ns | 1.491 ns | 163.44 ns | 0.0238 |     200 B |
|               Classic_RefTypes |                  New |  75.42 ns | 0.135 ns | 0.199 ns |  75.33 ns | 0.0181 |     152 B |
|             Classic_ValueTypes |                  New |  80.57 ns | 0.123 ns | 0.180 ns |  80.57 ns | 0.0401 |     336 B |
|   LoggerMessageDefine_RefTypes |                  New |  58.93 ns | 0.273 ns | 0.400 ns |  58.68 ns | 0.0172 |     144 B |
| LoggerMessageDefine_ValueTypes |                  New |  61.95 ns | 0.247 ns | 0.354 ns |  62.11 ns | 0.0391 |     328 B |
|        ClassicCodeGen_RefTypes |                  New |  58.33 ns | 0.200 ns | 0.287 ns |  58.22 ns | 0.0172 |     144 B |
|      ClassicCodeGen_ValueTypes |                  New |  61.40 ns | 0.252 ns | 0.370 ns |  61.53 ns | 0.0391 |     328 B |
|         ModernCodeGen_RefTypes |                  New |  40.77 ns | 0.138 ns | 0.193 ns |  40.92 ns |      - |         - |
|       ModernCodeGen_ValueTypes |                  New |  52.98 ns | 0.151 ns | 0.222 ns |  52.94 ns | 0.0200 |     168 B |
|               Classic_RefTypes |      NewWithSampling | 184.89 ns | 0.435 ns | 0.623 ns | 184.84 ns | 0.0219 |     184 B |
|             Classic_ValueTypes |      NewWithSampling | 196.86 ns | 1.227 ns | 1.836 ns | 196.87 ns | 0.0439 |     368 B |
|   LoggerMessageDefine_RefTypes |      NewWithSampling | 172.37 ns | 0.444 ns | 0.622 ns | 172.47 ns | 0.0210 |     176 B |
| LoggerMessageDefine_ValueTypes |      NewWithSampling | 174.10 ns | 0.379 ns | 0.532 ns | 174.27 ns | 0.0429 |     360 B |
|        ClassicCodeGen_RefTypes |      NewWithSampling | 173.22 ns | 1.112 ns | 1.594 ns | 173.91 ns | 0.0210 |     176 B |
|      ClassicCodeGen_ValueTypes |      NewWithSampling | 179.90 ns | 4.670 ns | 6.845 ns | 185.61 ns | 0.0429 |     360 B |
|         ModernCodeGen_RefTypes |      NewWithSampling | 151.22 ns | 0.216 ns | 0.317 ns | 151.14 ns | 0.0038 |      32 B |
|       ModernCodeGen_ValueTypes |      NewWithSampling | 164.27 ns | 0.295 ns | 0.433 ns | 164.18 ns | 0.0238 |     200 B |
|               Classic_RefTypes |     NewWithEnrichers |  79.40 ns | 0.978 ns | 1.433 ns |  80.56 ns | 0.0181 |     152 B |
|             Classic_ValueTypes |     NewWithEnrichers |  82.96 ns | 0.615 ns | 0.882 ns |  82.97 ns | 0.0401 |     336 B |
|   LoggerMessageDefine_RefTypes |     NewWithEnrichers |  61.99 ns | 0.663 ns | 0.972 ns |  61.28 ns | 0.0172 |     144 B |
| LoggerMessageDefine_ValueTypes |     NewWithEnrichers |  64.49 ns | 0.295 ns | 0.413 ns |  64.32 ns | 0.0391 |     328 B |
|        ClassicCodeGen_RefTypes |     NewWithEnrichers |  60.76 ns | 0.122 ns | 0.183 ns |  60.79 ns | 0.0172 |     144 B |
|      ClassicCodeGen_ValueTypes |     NewWithEnrichers |  64.17 ns | 0.130 ns | 0.195 ns |  64.17 ns | 0.0391 |     328 B |
|         ModernCodeGen_RefTypes |     NewWithEnrichers |  43.94 ns | 0.177 ns | 0.254 ns |  44.12 ns |      - |         - |
|       ModernCodeGen_ValueTypes |     NewWithEnrichers |  56.57 ns | 0.523 ns | 0.733 ns |  56.83 ns | 0.0200 |     168 B |
|               Classic_RefTypes | NewWi(...)pling [27] | 190.22 ns | 0.597 ns | 0.856 ns | 190.35 ns | 0.0219 |     184 B |
|             Classic_ValueTypes | NewWi(...)pling [27] | 197.92 ns | 0.623 ns | 0.933 ns | 198.02 ns | 0.0439 |     368 B |
|   LoggerMessageDefine_RefTypes | NewWi(...)pling [27] | 176.55 ns | 1.215 ns | 1.743 ns | 177.66 ns | 0.0210 |     176 B |
| LoggerMessageDefine_ValueTypes | NewWi(...)pling [27] | 175.56 ns | 0.500 ns | 0.716 ns | 175.46 ns | 0.0429 |     360 B |
|        ClassicCodeGen_RefTypes | NewWi(...)pling [27] | 167.96 ns | 1.379 ns | 1.934 ns | 167.98 ns | 0.0210 |     176 B |
|      ClassicCodeGen_ValueTypes | NewWi(...)pling [27] | 173.94 ns | 0.709 ns | 1.039 ns | 174.08 ns | 0.0429 |     360 B |
|         ModernCodeGen_RefTypes | NewWi(...)pling [27] | 150.95 ns | 0.723 ns | 1.082 ns | 150.75 ns | 0.0038 |      32 B |
|       ModernCodeGen_ValueTypes | NewWi(...)pling [27] | 165.48 ns | 0.387 ns | 0.579 ns | 165.53 ns | 0.0238 |     200 B |

## 2026 Bottom-K sampling results

These results cover the Bottom-K implementation and benchmark suites described in
[design.md](design.md). Runs used Windows 11 under Hyper-V, .NET 10 x64 RyuJIT AVX2,
two launches, ten warmup iterations, and fifteen measured iterations. Tiered compilation
was disabled. Treat the figures as directional because virtualization can affect timing.
The results were rerun after correcting benchmark cleanup so adaptive frequency state is
preserved between iterations.

Some saved artifacts use the implementation's earlier `Cckr` name. Those rows correspond
to the Bottom-K implementation in this project.

### Serialized exporter

This is the most representative CPU and allocation comparison because the provider formats
and serializes retained logs to UTF-8 JSON. Bottom-K and random sampling both target 1%
retention. Times and allocations are for the complete input batch.

| Strategy | Input logs | Mean | Allocated | Ratio to no sampling |
| --- | ---: | ---: | ---: | ---: |
| No sampling | 10,000 | 9.818 ms | 2,805.66 KB | 1.00 |
| Random 1% | 10,000 | 3.119 ms | 966.35 KB | 0.31 |
| Bottom-K 1% | 10,000 | 2.932 ms | 769.30 KB | 0.30 |
| No sampling | 20,000 | 19.813 ms | 5,618.16 KB | 1.00 |
| Random 1% | 20,000 | 5.988 ms | 1,927.37 KB | 0.29 |
| Bottom-K 1% | 20,000 | 5.880 ms | 1,518.05 KB | 0.30 |

At 20,000 logs, Bottom-K and random 1% sampling had similar execution times, while Bottom-K
allocated approximately 21% less managed memory. Both sampling strategies were substantially
cheaper than serializing every log.

### Category cardinality

This suite processes 20,000 logs at 1% retention while varying the number of categories.

| Categories | Strategy | Mean | Allocated |
| ---: | --- | ---: | ---: |
| 50 | Random 1% | 5.736 ms | 1.88 MB |
| 50 | Bottom-K 1% | 6.290 ms | 1.63 MB |
| 100 | Random 1% | 5.997 ms | 1.88 MB |
| 100 | Bottom-K 1% | 7.070 ms | 1.72 MB |
| 200 | Random 1% | 5.980 ms | 1.88 MB |
| 200 | Bottom-K 1% | 7.078 ms | 1.82 MB |

Bottom-K remained stable from 100 through 200 categories, but was 10% to 18% slower than
random sampling in this suite. It allocated less than random sampling at every cardinality,
although allocation increased because it maintains one bounded reservoir per category.

### Multithreaded contention

This suite processes 20,000 logs across 100 shared categories. The measured operation includes
the Bottom-K flush.

| Workers | Strategy | Mean | Allocated |
| ---: | --- | ---: | ---: |
| 1 | Random 1% | 5.614 ms | 1.84 MB |
| 1 | Bottom-K 1% | 6.002 ms | 1.71 MB |
| 4 | Random 1% | 2.221 ms | 1.84 MB |
| 4 | Bottom-K 1% | 3.459 ms | 1.72 MB |
| 8 | Random 1% | 2.216 ms | 1.84 MB |
| 8 | Bottom-K 1% | 3.833 ms | 1.71 MB |

Bottom-K allocated less but showed increasing synchronization cost relative to random sampling
at four and eight workers.

### Retained memory

Each scenario ran in a fresh worker process. `Retained managed` is live managed memory after
a forced full collection; `Peak managed` is the largest observed managed-heap increase while
processing the batch.

| Pipeline | Input logs | Retained managed | Peak managed |
| --- | ---: | ---: | ---: |
| No sampling | 10,000 | ~0 MiB | 0.59 MiB |
| Random 1% | 10,000 | ~0 MiB | 1.05 MiB |
| Global buffer | 10,000 | 3.26 MiB | 4.46 MiB |
| Bottom-K adaptive | 10,000 | 0.20 MiB | 1.18 MiB |
| Bottom-K retain all | 10,000 | 3.79 MiB | 5.02 MiB |
| No sampling | 20,000 | ~0 MiB | 0.84 MiB |
| Random 1% | 20,000 | ~0 MiB | 2.09 MiB |
| Global buffer | 20,000 | 6.53 MiB | 9.16 MiB |
| Bottom-K adaptive | 20,000 | 0.20 MiB | 1.96 MiB |
| Bottom-K retain all | 20,000 | 7.66 MiB | 10.20 MiB |

Adaptive Bottom-K retained approximately 0.20 MiB at both traffic volumes because its sample
capacity was fixed. Retain-all configurations use a per-category capacity sized to each
category's share of the workload; they intentionally scale with input volume and are included
only to expose the upper-bound buffering cost.

### Category-policy cost

The following run used .NET 10.0.12. Each row processes the complete input batch and all
patterns miss, so output volume remains equivalent.

| Policy | 10,000 logs | 20,000 logs |
| --- | ---: | ---: |
| No category policy | 3.879 ms | 6.423 ms |
| Two wildcard misses | 4.806 ms | 7.737 ms |
| 100 wildcard misses | 27.766 ms | 54.854 ms |
| 100 exact misses | 4.124 ms | 6.973 ms |

Exact category names use a case-insensitive frozen set and therefore remain effectively
constant-time. Wildcard patterns are checked sequentially; use them selectively for specific
namespace families.
