# .NET 10 vs Rust memory benchmark

Measured 2026-10-25 (Asia/Saigon). Batch: 2026-10-25T07-52-47-811Z.
Both APIs were freshly rerun; old benchmark RAM samples are not mixed in.
Each scenario gets a new target container. All numbers below are MiB unless stated.

## API process RSS

Each value is the median across three independent repetitions of the named
per-run metric. Load maximum means maximum 1-second sample during measured traffic,
not a guaranteed instantaneous peak. RSS includes resident shared mappings.

| CCU | .NET baseline RSS | Rust baseline RSS | .NET load median RSS | Rust load median RSS | .NET load sampled max RSS | Rust load sampled max RSS | Rust lower sampled max |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 100 | 65.75 | 3.00 | 82.00 | 5.00 | 82.88 | 5.00 | 94.0% |
| 1000 | 66.13 | 3.00 | 106.59 | 24.88 | 107.72 | 24.88 | 76.9% |
| 5000 | 65.75 | 3.00 | 174.64 | 115.50 | 179.73 | 115.50 | 35.7% |

Rust uses less API-process RSS at every tested CCU for these implementations.
This is NOT the same claim as lower total-container memory by the same percentage.

## Whole-container accounting

Working set here is max(0, cgroup memory.current - inactive_file), computed from
raw cgroup v2 samples. Container metrics include API, sampler, charged kernel/socket
memory and cache. RSS and cgroup charge ownership for shared pages differ; cgroup
current can be below process RSS. Do not add the metrics or treat them interchangeably.

| CCU | .NET sampled max working set | Rust sampled max working set | .NET lifetime cgroup peak | Rust lifetime cgroup peak |
|---:|---:|---:|---:|---:|
| 100 | 34.13 | 4.82 | 40.69 | 6.00 |
| 1000 | 64.39 | 32.55 | 67.51 | 34.22 |
| 5000 | 161.24 | 160.95 | 166.00 | 162.21 |

At 5000 CCU, median sampled max working sets are 161.24 MiB (.NET) and
160.95 MiB (Rust): effectively similar in this short experiment. The 0.29 MiB
difference is not evidence of a robust whole-container advantage. Although Rust
has about 35.7% lower process RSS here, it does NOT save 35.7% of total container RAM.
Lifetime cgroup peaks are last observed memory.peak values before stopping,
including startup/warmup/idle, not load-only peaks. No swap was observed.

## After load

After loadgen finishes its drain/cooldown, observe another five idle seconds.
No forced GC or allocator trimming is performed. Baseline is after /health,
before the parser's baseline validation/warmup. These phases are not equivalent
application states, so a post-load increase is not proof of a memory leak.

| CCU | .NET post-idle RSS | Rust post-idle RSS | .NET process VmHWM | Rust process VmHWM |
|---:|---:|---:|---:|---:|
| 100 | 89.75 | 4.67 | 89.75 | 5.00 |
| 1000 | 114.47 | 24.19 | 114.47 | 24.88 |
| 5000 | 185.73 | 112.73 | 185.73 | 115.50 |

VmHWM is OS-reported high-water RSS over the observed process lifetime,
including startup/warmup/idle. Its scope differs from the load-only sampled max.

## Throughput context

Equal CCU is NOT equal completed request rate. Memory measurements must be read
with throughput, not interpreted as equal-RPS allocation efficiency.

| CCU | .NET success req/s | Rust success req/s | .NET p95 ms | Rust p95 ms |
|---:|---:|---:|---:|---:|
| 100 | 4570.73 | 10630.30 | 67.81 | 11.89 |
| 1000 | 4794.92 | 10958.80 | 310.79 | 181.60 |
| 5000 | 3988.28 | 10066.60 | 1808.63 | 1133.04 |

2,738,056 successful measured requests; zero measured errors, zero OOMKilled,
zero restarts. All 18 scenarios reach the configured peak active client calls.
Load generator CPU averages range 0.53-2.39 cores, below its 4-core quota.
16 smoke cases per API passed before this batch. Rust exits 137 after Docker's
3-second stop grace period due to no PID1 SIGTERM handler; OOMKilled=false.
.NET exits 0. Shutdown occurs outside measured request windows.

## Method and limits

- .NET runtime/ASP.NET Core 10.0.12, Minimal API + System.Text.Json, Release JIT;
  unchanged handler from the earlier benchmark. No explicit GC or heap tuning.
- Rust compiler image 1.90, Axum 0.8.6, serde 1.0.229, serde_json 1.0.154,
  Tokio 1.54.1, one worker, release thin LTO; Cargo.lock pins dependencies.
- Both APIs: 1 CPU quota / 1 GiB memory; loadgen: 4 CPU / 2 GiB. Quotas checked
  in every fresh scenario's inspect. No pinned/dedicated cores or isolated host.
- Same 4096-byte payload SHA256 EF3E45CB72274671D9F74A7C0F6F05D81C67990AAA202A89784B899562C6FFF5.
  UTF-8 text -> typed object -> JSON echo. No DB, cache, TLS, compression, auth,
  custom application middleware or request logging. .NET framework routing remains.
- 100/1000/5000 CCU, 5s pre-load idle, 5s warmup, 20s measurement, loadgen drain
  and 2s cooldown, then 5s idle. Three repetitions, reverse order on even ones;
  three repetitions are not fully order-balanced. All 18 target container IDs differ.
- A small in-container shell sampler reads /proc/1/status and cgroup v2 about
  every second. Its overhead is counted in cgroup metrics for both APIs but not
  in PID1 RSS. Each measured scenario has 20-21 samples. Spikes can be missed.
- Fresh-process .NET JIT/PGO need not be fully stable after five seconds. These
  short local runs cannot prove a production memory budget or language-wide claim.
- Existing unrelated containers remain untouched. Same concurrency, not equal RPS.
  No memory-leak claim or performance SLA/pass threshold is inferred.
- RPS includes drain; p95 is median of per-run percentiles, not pooled p95.
  Every response is read/status-checked; semantic equality is sampled about
  1/1024 responses per worker. Client 15s timeout covers headers, not a separate
  body deadline. Histogram upper-bound buckets have about 1% quantization.

## Evidence and rerun

Run node Run-Benchmark.mjs, then node Analyze-Memory.mjs. See README.md.
Per-run data: validation/memory-results.csv and validation/memory-results.json.
Summary: validation/memory-summary.json. Raw per-second samples, per-scenario
loadgen results and pre/post inspect: results/2026-10-25T07-52-47-811Z/.
validation/source-hashes.json identifies the exact measured sources/payload.
The runner's console REP column is its local one-run loop counter (1); JSON/CSV
Repetition and manifest contain the actual outer repetition 1/2/3.
