# .NET 10: 1 / 2 / 4 CPU benchmark

Measured locally on 2026-10-25 (Asia/Saigon). Batch: 2026-10-25T09-27-00-308Z.
.NET/ASP.NET Core 10.0.13, SDK 10.0.402, Ubuntu Resolute, Release JIT.
All three CPU variants use the identical built API image, handler and payload.
RAM stays fixed at 1 GiB. Only CPU quota changes; runtime defaults are preserved.

## Throughput: medians of three repetitions

| CCU | 1 CPU success req/s | 2 CPU success req/s | 4 CPU success req/s | 2 CPU / 1 CPU | 4 CPU / 1 CPU |
|---:|---:|---:|---:|---:|---:|
| 100 | 4601.95 | 9700.93 | 12211.70 | 2.11x | 2.65x |
| 1000 | 2048.42 | 8094.71 | 13880.23 | 3.95x | 6.78x |
| 5000 | 3368.73 | 9507.82 | 11003.66 | 2.82x | 3.27x |

At 5000 CCU, same-batch median throughput is 2.82x higher with 2 CPUs and
3.27x higher with 4 CPUs than with 1 CPU. Moving from 2 to 4 CPUs adds about
15.7% median throughput here, NOT 100%. These are observed ratios, not promised
production speedups. The baseline and other runs vary substantially.

IMPORTANT: variability is large, particularly at 1000 CCU. The 1-CPU result spans
884.73-5189.20 req/s across just three runs. Do not read the apparently superlinear
1000-CCU ratios as CPU efficiency. Default GC mode changes and an unstable
fresh-process/shared-host baseline confound any CPU-only interpretation.

## p95 latency and memory at 5000 CCU

p95 values are medians of per-run percentiles, not pooled percentiles. RSS and
container working set below are medians of per-run maxima sampled about once
per second during measured traffic, not guaranteed instantaneous peaks.

| CPU quota | p95 ms | API sampled max RSS MiB | Container sampled max working set MiB | Sampled mean target CPU cores |
|---:|---:|---:|---:|---:|
| 1 | 2319.44 | 180.64 | 157.82 | 1.00 |
| 2 | 768.62 | 251.15 | 231.85 | 1.98 |
| 4 | 849.03 | 235.43 | 194.75 | 3.10 |

4 CPUs have higher median throughput, but p95 is about 10.5% worse than 2 CPUs
at 5000 CCU (849.03 vs 768.62ms). More CPU does not improve every measured
metric in this experiment. Sampled max RSS is not monotonic with CPU count;
4 CPUs use less than 2 CPUs in these three 5000-CCU repetitions.

## Full memory/latency matrix

All memory values are MiB. Rows remain medians of the three corresponding
per-run metrics. Baseline is after /health, before parser baseline/warmup.

| CCU | CPUs | p95 ms | Baseline RSS | Load temporal median RSS | Load sampled max RSS | Post-idle RSS | Container working set sampled max |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 100 | 1 | 74.90 | 68.00 | 84.19 | 85.50 | 92.00 | 35.02 |
| 100 | 2 | 29.99 | 69.25 | 90.65 | 91.06 | 90.88 | 41.53 |
| 100 | 4 | 18.42 | 70.13 | 98.95 | 99.26 | 99.01 | 49.52 |
| 1000 | 1 | 901.27 | 68.13 | 104.76 | 106.64 | 113.76 | 62.05 |
| 1000 | 2 | 360.82 | 68.88 | 114.16 | 121.38 | 121.62 | 75.82 |
| 1000 | 4 | 150.32 | 69.75 | 125.24 | 126.65 | 125.89 | 80.95 |
| 5000 | 1 | 2319.44 | 67.88 | 168.17 | 180.64 | 186.42 | 157.82 |
| 5000 | 2 | 768.62 | 70.00 | 193.17 | 251.15 | 193.45 | 231.85 |
| 5000 | 4 | 849.03 | 69.63 | 177.85 | 235.43 | 178.77 | 194.75 |

Process RSS includes resident shared mappings; cgroup shared-page/cache charges
use different ownership rules. Working set here is max(0, memory.current minus
inactive_file), including charged kernel/socket/cache memory and sampler overhead.
It can be below process RSS. Do not sum the metrics or equate either to managed
heap size. Raw samples also include OS-reported VmHWM and cgroup lifetime
memory.peak, which include startup/warmup/idle, not load-only peaks.
No swap was observed. A five-second post-idle increase is not a memory-leak test.

## Variation and resource context

| CCU | CPUs | Req/s min-max | API RSS sampled max min-max MiB | Median sampled target CPU cores |
|---:|---:|---:|---:|---:|
| 100 | 1 | 4592.63-4719.77 | 85.00-85.75 | 1.00 |
| 100 | 2 | 9497.90-15076.89 | 91.02-91.63 | 1.99 |
| 100 | 4 | 9899.79-18024.31 | 99.14-99.90 | 3.34 |
| 1000 | 1 | 884.73-5189.20 | 104.94-113.13 | 1.00 |
| 1000 | 2 | 4408.53-10241.63 | 117.63-130.20 | 1.99 |
| 1000 | 4 | 4024.59-14066.22 | 126.37-132.11 | 3.54 |
| 5000 | 1 | 2857.00-3976.83 | 175.38-185.67 | 1.00 |
| 5000 | 2 | 5406.72-11398.15 | 248.02-264.43 | 1.98 |
| 5000 | 4 | 8910.84-12683.73 | 214.20-237.38 | 3.10 |

Docker reports eight logical CPUs for this host. Target CPU quota is not a pinned
or dedicated core. Target CPU estimates come from cgroup usage_usec deltas between
first/last samples inside the measured request window, excluding interval edges;
sampler overhead is included. Small apparent quota overshoots can occur from
sampling/timestamp timing. Raw throttling counters are retained.

The native load generator has 4 CPU / 2 GiB, with measured average client CPU
0.37-2.37 cores across scenarios. There is no obvious full client CPU-quota
saturation, but this does not prove the generator/network has unlimited capacity.
At 5000 CCU the 4-CPU target averages about 3.10 used cores rather than 4.00.
No profiling-based cause is assigned to the remaining scaling limit.

## Observed runtime modes

Startup logs from every scenario confirm:

| CPU quota | Environment.ProcessorCount | GCSettings.IsServerGC |
|---:|---:|---|
| 1 | 1 | false (Workstation) |
| 2 | 2 | true (Server) |
| 4 | 4 | true (Server) |

All report TotalAvailableMemoryBytes=805306368. No GC mode/heap count,
thread-pool, processor-count, memory-hard-limit or PGO override is supplied.
Therefore this compares deployment configurations with runtime defaults, NOT
raw CPU count while holding GC mode fixed. No causal GC-only claim is made.

## Correctness and method

- 27 measured scenarios, 4,439,206 successful measured requests, zero measured
  errors, zero OOMKilled, zero restarts, all API exits 0. No performance SLA supplied.
- 16 smoke cases per CPU configuration pass before measurements. All measured
  peak active client calls reach the configured CCU. Functional error cases are
  separate from measured request errors.
- Same common API image verified across all 27 unique fresh target containers.
  Every scenario's inspect confirms CPU quota 1/2/4 and memory limit 1073741824.
  Other API variants are stopped during measured runs; unrelated services untouched.
- Same 4096-byte generated UTF-8 JSON text payload, typed deserialize/JSON echo,
  SHA256 EF3E45CB72274671D9F74A7C0F6F05D81C67990AAA202A89784B899562C6FFF5.
- Minimal API/Kestrel/System.Text.Json, Release JIT. No database/cache/TLS,
  compression/auth/artificial delay/custom middleware or per-request logging.
  Framework routing remains. One startup console JSON diagnostic only.
- 100/1000/5000 closed-loop CCU, no think time, three repetitions/configuration.
  Cyclic CPU orders [1,2,4], [2,4,1], [4,1,2] balance sequence positions,
  not all six pairwise permutations. Each CPU occurs once per position per CCU.
- Per scenario: health -> 5s idle -> loadgen semantic baseline -> 5s warmup ->
  20s measured traffic -> drain and 2s cooldown -> 5s idle -> stop target.
  Five seconds need not stabilize tiered JIT/PGO on fresh .NET processes.
- Shell sampler reads PID1 RSS and cgroup memory/CPU about every second.
  It is excluded from PID1 RSS but included in target cgroup CPU and memory.
  Measured intervals have 19-21 samples; transient peaks can be missed.

## Interpretation limits

Same CCU is not equal RPS or per-request allocation profiling. Shared Docker
Desktop/WSL2 host is not isolated. Short measurements, three repetitions, changing
default GC mode and large run-to-run variation prevent confident production
capacity or universal linear-scaling claims. Longer balanced steady-state runs
and profiling would be needed before choosing a production CPU budget.

RPS includes drain; histograms use ~1% upper-bound buckets. Every response is
fully read/status-checked; semantic equality at baseline and about 1/1024 worker
responses. PeakInflight includes client/network queues, not CPU parallelism.
15s HttpClient timeout covers headers only; body has cancellation but no separate
hard deadline. Old .NET/Rust/RC2 batch numbers are NOT mixed into this experiment.

## Evidence and rerun

Run node Run-Benchmark.mjs then node Analyze-Memory.mjs. See README.md.
Per-run CSV/JSON: validation/memory-results.csv and memory-results.json.
Summary JSON: validation/memory-summary.json. Raw RSS/cgroup/CPU samples, startup
GC diagnostics, pre/post inspect and loadgen results: results/2026-10-25T09-27-00-308Z/.
Source hashes, host/runtime/OS information and audit are in validation/.
