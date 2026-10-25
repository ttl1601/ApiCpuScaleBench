# .NET 10 vs .NET 11 RC2 throughput and RAM

Local measurement on 2026-10-25 (Asia/Saigon). Batch: 2026-10-25T08-18-02-251Z.
.NET 10: runtime/ASP.NET Core 10.0.13, SDK 10.0.402.
.NET 11 RC2: runtime/ASP.NET Core 11.0.0-rc.2.26504.105,
SDK image 11.0.100-rc.2. Both runtime images report Ubuntu 26.04.1 LTS,
Resolute Raccoon, linux-x64. This is RC2, NOT .NET 11 general availability.
Versions were verified inside the running containers, not inferred from tags.

## Median of three repetitions

Throughput is successful requests/second; delta is RC2 relative to .NET 10.
RAM is median of the three per-run maximum one-second PID1 RSS samples during
measured traffic. It is not an instantaneous guaranteed peak or managed heap.

| CCU | .NET 10 req/s | .NET 11 RC2 req/s | RC2 throughput delta | .NET 10 RSS MiB | RC2 RSS MiB | RC2 RSS delta |
|---:|---:|---:|---:|---:|---:|---:|
| 100 | 4514.42 | 4810.58 | +6.6% | 85.13 | 84.25 | -1.0% |
| 1000 | 4640.88 | 4065.55 | -12.4% | 107.60 | 109.46 | +1.7% |
| 5000 | 4045.80 | 3860.63 | -4.6% | 180.59 | 190.94 | +5.7% |

Within this workload, RC2 does not improve throughput consistently. Its median
throughput is about 6.6% higher at 100 CCU but 12.4% lower at 1000 and 4.6% lower
at 5000. At 5000 its sampled API RSS is about 5.7% higher, not lower.
This describes these short fresh-process API runs, not a general .NET 11 regression.
The 100-CCU throughput ranges overlap; three repeats do not establish significance.

## Latency and variability

Percentiles are medians of per-run p95 values, not a pooled p95.

| CCU | .NET 10 p95 ms | RC2 p95 ms | .NET 10 req/s range | RC2 req/s range |
|---:|---:|---:|---:|---:|
| 100 | 71.98 | 60.18 | 4501.46-4684.58 | 4198.16-4855.11 |
| 1000 | 326.64 | 379.22 | 4592.88-4684.23 | 3804.37-4331.92 |
| 5000 | 1686.94 | 1958.48 | 4014.39-4062.90 | 3711.49-3882.20 |

At 1000 and 5000 CCU, all three RC2 repetitions have lower throughput than all
three .NET 10 repetitions in this batch. This is useful local evidence, but the
host is not isolated and the five-second warmup need not stabilize tiered JIT/PGO.
Further sustained, balanced runs/profiling are needed before assigning a cause.

## Container memory is a different metric

Working set = max(0, memory.current - inactive_file) from raw cgroup v2 samples.
It includes charged kernel/socket/cache memory and the small monitoring shell.
Process RSS includes shared resident mappings. Cgroup ownership for shared pages
and image cache sharing differs, so container working set can be below RSS.
Do not add the two metrics, equate them, or interpret either as GC heap allocation.

| CCU | .NET 10 container sampled max working set MiB | RC2 container sampled max working set MiB | .NET 10 lifetime cgroup peak MiB | RC2 lifetime cgroup peak MiB |
|---:|---:|---:|---:|---:|
| 100 | 34.20 | 33.75 | 42.27 | 38.99 |
| 1000 | 60.79 | 66.41 | 66.66 | 69.52 |
| 5000 | 159.07 | 183.39 | 166.42 | 186.88 |

At 5000 CCU the sampled container working set is 159.07 MiB (.NET 10) versus
183.39 MiB (RC2), about 15.3% higher in this experiment. This is a sampled
working-set comparison, not a private physical-RAM measurement. The lifetime
memory.peak includes startup, warmup and idle, not only measured traffic.

## Idle and after load

Baseline is after /health but before parser baseline validation and warmup.
Post-idle is five seconds after loadgen drain/cooldown. No forced GC/trim is used.
Post-load growth alone is not evidence of a leak or proof memory will never return.

| CCU | .NET 10 baseline RSS MiB | RC2 baseline RSS MiB | .NET 10 post-idle RSS MiB | RC2 post-idle RSS MiB |
|---:|---:|---:|---:|---:|
| 100 | 67.13 | 67.38 | 91.88 | 88.75 |
| 1000 | 67.75 | 67.00 | 113.98 | 114.13 |
| 5000 | 67.00 | 66.88 | 187.00 | 195.06 |

## Correctness and setup

- 18 measured scenarios, 1,570,609 successful measured requests, zero measured
  errors, zero OOMKilled, zero restarts, no swap. Both APIs stop with exit 0.
- Both implementations pass 16 smoke cases before measurement. All measured
  peak active client calls equal configured CCU. No performance SLA was supplied.
- Identical Program.cs SHA256 for both APIs, built as net10.0 and net11.0.
  Minimal API / Kestrel / System.Text.Json, Release JIT, runtime defaults.
  No custom middleware/auth/cache/database/TLS/compression/request logging.
  Framework routing/endpoint machinery remains. No NativeAOT/source generation.
- Same payload: 4096 bytes, UTF-8 text -> typed object -> JSON echo,
  SHA256 EF3E45CB72274671D9F74A7C0F6F05D81C67990AAA202A89784B899562C6FFF5.
- Both targets: quota 1 CPU / limit 1 GiB. Dedicated native .NET loadgen:
  4 CPU / 2 GiB, unchanged across targets, measured client CPU 0.49-0.68 cores.
- Each of the 18 scenarios uses a unique freshly created target container;
  the other target is stopped. This prevents earlier CCU levels retaining memory.
  Existing unrelated host containers are not stopped.
- 100/1000/5000 CCU closed loop, no think time; 5s idle baseline, 5s warmup,
  20s measured, drain plus 2s cooldown, then 5s idle. Three repetitions with
  reversed order on even reps; odd repetition count is not fully order-balanced.
- A small shell sampler inside each API cgroup reads PID1 RSS and cgroup v2
  about every second. Overhead is counted in cgroup memory/CPU for both targets,
  excluded from PID1 RSS. Each measured interval has 19-21 samples.

## Boundaries

Same CCU does not mean equal completed RPS. This is not equal-throughput RAM or
per-request allocation profiling. CPU quota is not a pinned or dedicated core.
Docker Desktop/WSL2 host and other services are not isolated. Five seconds warmup
on fresh .NET processes is insufficient to guarantee steady tiered-PGO state.
SDK/compiler, runtime, ASP.NET and serializer versions all change together;
this cannot isolate a GC-only/JIT-only effect. RC2 may differ from eventual GA.

RPS denominator includes drain. Histogram p95 uses ~1% upper-bound buckets.
All responses read/status-checked; semantic equality at baseline and approximately
1/1024 successful responses per worker during load. PeakInflight includes queues,
not server CPU parallelism. Client timeout is 15s until response headers; body has
cancellation but no separate hard deadline. No arbitrary pass/fail threshold.

## Evidence and rerun

Run node Run-Benchmark.mjs then node Analyze-Memory.mjs. See README.md.
Per-run measurements: validation/memory-results.csv / memory-results.json.
Summaries: validation/memory-summary.json. Raw one-second samples, actual loadgen
results and pre/post inspect: results/2026-10-25T08-18-02-251Z/.
Runtime/OS checks, official release metadata and source hashes are in validation/.
Do not mix numbers from older .NET 10/Rust/C++ batches into this version comparison.
