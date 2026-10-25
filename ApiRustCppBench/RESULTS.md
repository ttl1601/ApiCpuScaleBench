# Rust vs C++ HTTP JSON benchmark results

Measured locally on 2026-10-25 (Asia/Saigon), run 20261025T073957735Z-run.
Both implementations were rerun in this batch; no results borrowed from earlier tests.

## Median of three repetitions

| CCU | C++ success RPS | Rust success RPS | Rust/C++ | C++ p95 ms | Rust p95 ms | C++ p99 ms | Rust p99 ms |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 100 | 3163.3 | 7065.2 | 2.23x | 40.02 | 18.60 | 53.41 | 21.17 |
| 1000 | 4053.8 | 9276.2 | 2.29x | 295.71 | 217.22 | 371.75 | 252.18 |
| 5000 | 5056.0 | 11523.5 | 2.28x | 1289.50 | 1067.37 | 1424.41 | 1226.92 |

These are medians of per-run percentiles, NOT pooled request percentiles.
Rust has higher throughput at every tested level in this implementation pair.
This does NOT prove Rust is intrinsically faster than C++ or all C++ libraries.

## Setup

- Each API: Docker Linux container, 1 CPU quota, 1 GiB RAM. No physical-core pinning.
- C++20: GCC 14.2.0, Drogon 1.9.0, nlohmann/json 3.11.3; Release -O3 -flto;
  one Drogon I/O worker. Dynamic framework binary comes from Debian packages.
- Rust 1.90: Axum 0.8.6, serde 1.0.229, serde_json 1.0.154, Tokio 1.54.1;
  release thin LTO, one codegen unit, one Tokio worker, Cargo.lock pinned.
- Separate native .NET load generator: 4 CPU / 2 GiB. Client CPU averages
  approximately 1.15-2.11 cores per scenario, below its 4-core quota.
- POST /parse: owned UTF-8 text -> typed object -> JSON echo. Payload 4096 bytes,
  24 nested items, identical SHA256 EF3E45CB72274671D9F74A7C0F6F05D81C67990AAA202A89784B899562C6FFF5.
- Required/non-null fields, strict int32 bounds, unknown fields rejected,
  case-sensitive names. C++ uses a DOM before and after typed conversion;
  Rust serde deserializes directly. This additional DOM cost is part of the
  selected C++ implementation, not evidence about language/compiler alone.
- HTTP/1.1 keepalive; no TLS, database, cache or handler logging.
- Closed-loop 100/1000/5000 concurrent client calls, 20s measured, 5s warmup,
  2s cooldown, 3 repetitions. Sequential targets, reversed on even repetitions.

## Correctness and errors

- 2,451,945 successful measured requests, zero measured errors.
- All measured peak active client calls reach the configured CCU.
- 16 smoke cases and 10 additional live boundary cases per API passed.
- Postman: 3 requests / 4 assertions per API passed (local runs, no cloud upload).
- Oversized smoke requests use Expect: 100-continue so the client can observe
  an early HTTP 413 instead of a broken pipe while still uploading a rejected body.
  The measured 4096-byte workload does not set this header.
- No OOMKilled and no restart in post-run inspect. C++ stops with exit 0.
  Rust has no PID1 SIGTERM handler; Docker stop ends it with exit 137 after the
  grace period. This occurs after measurement, and OOMKilled=false.

## Variation and sampled RAM

| CCU | C++ RPS range | Rust RPS range | C++ sampled max MiB | Rust sampled max MiB |
|---:|---:|---:|---:|---:|
| 100 | 2337-3496 | 6616-8642 | 6.613 | 4.371 |
| 1000 | 3624-4232 | 8884-11442 | 31.74 | 32.02 |
| 5000 | 4094-5065 | 10106-11977 | 141.6 | 156.6 |

RAM values are sampled Docker working set during measured intervals, not RSS or
true peaks. At 5000 CCU C++ p95 spans 1078.05-2296.48ms and Rust p95 spans
975.94-1328.58ms; Rust does not win every individual run's p95/p99.
The host is shared and not isolated. Existing unrelated services were not stopped;
a testcontainers Ryuk service was also observed during the run. No claim of
controlled dedicated-host conditions is made. Three short repeats do not establish
statistical confidence or production capacity. There is no supplied SLA/pass threshold.

## Evidence and rerun

Run ./Run-Benchmark.ps1 from this directory. See README.md and CONTRACT.md.
Raw measurements: results/20261025T073957735Z-run/ and validation/results.csv.
Validation contains source hashes, package/compiler versions, pre/post container
inspect, Docker host information, resource samples, Postman and benchmark logs.
RPS denominator includes drain, histogram percentiles use ~1% buckets, and semantic
validation samples about 1/1024 responses per worker after baseline checks.
15s client timeout ends at headers; body has cancellation but no separate deadline.
Do not directly rank this batch against old Go/.NET/Rust batches on the shared host.
