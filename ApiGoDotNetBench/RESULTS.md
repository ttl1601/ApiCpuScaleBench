# Go vs .NET 10 - JSON API, 1 CPU / 1 GiB

Actual paired local Docker benchmark on 2026-10-25 (Asia/Saigon). This batch
reruns BOTH APIs. Old Rust/.NET measurements are not used for this comparison.

## Setup

- .NET API: ASP.NET Core 10.0.12 Minimal API, Kestrel, System.Text.Json, Release JIT (no NativeAOT/source generation).
- Go API: Go 1.27.3, standard net/http ServeMux and encoding/json, CGO_ENABLED=0, GOMAXPROCS=1. No third-party framework or JSON library.
- Both: no custom middleware/auth/request logging/compression/TLS/database/cache. .NET still has default framework routing/endpoint middleware.
- Each API: CPU quota 1 core, memory limit 1 GiB (1073741824 bytes). Docker inspect and cgroup files verify cpu.max=100000 100000 and memory.max=1073741824. Not pinned/dedicated physical cores.
- Separate identical .NET load generator: 4 CPU / 2 GiB; outside API quotas.
- Same 4096-byte input, 24 nested items, ASCII text/strings, booleans, int32 and finite doubles. SHA256: EF3E45CB72274671D9F74A7C0F6F05D81C67990AAA202A89784B899562C6FFF5.
- POST /parse: UTF-8 bytes -> owned text string -> typed object -> JSON echo. Required/non-null and unknown-field checks on both. Go pointer fields preserve missing/null vs zero/false; .NET required fields plus explicit reference checks.
- Both APIs accept case-insensitive ASCII field names in THIS batch. .NET explicitly enables PropertyNameCaseInsensitive=true to match Go. Prior Rust batch had case-sensitive .NET; no cross-batch RPS attribution is made.
- HTTP/1.1 keep-alive, closed-loop zero think time. 100 / 1000 / 5000 CCU; 20s measured + 5s warmup + 2s cooldown, 3 repetitions/API/CCU. Sequential targets; reverse target order on even repetitions. Odd repetition count is not fully order-balanced.
- Go appends a newline when encoding JSON; response JSON equivalence is required, not byte-identical formatting.

## Summary

RPS = successful requests / actual wall time including drain. Each RPS is the
median of three repetitions. Each p95 is the median of the three per-run p95s,
NOT a pooled p95. Ratio >1 means .NET median throughput is higher.

| CCU | .NET request/s | Go request/s | .NET p95 ms | Go p95 ms | .NET/Go RPS |
|---|---:|---:|---:|---:|---:|
| 100 | 2673.0 | 2246.0 | 92.31 | 91.40 | 1.19x |
| 1000 | 5221.0 | 3007.1 | 368.07 | 472.02 | 1.74x |
| 5000 | 5610.2 | 3298.5 | 1133.04 | 2099.76 | 1.70x |

Measured success: 1,349,946; measured errors: 0. Warmup errors, if any, are logged separately and not pooled into measured results.

At 5000 CCU, p95 spans 1099.72-3665.78 ms for .NET and 1978.07-4892.00 ms for Go. A median hides these slow runs; do not treat the median as a stable SLA or maximum capacity.

Both APIs completed without OOMKilled or restarts. Both benchmark containers were stopped after measurement with exit code 0; unrelated containers and previous benchmark artifacts were left unchanged. The load generator runtime is .NET 10.0.13, distinct from the API ASP.NET Core 10.0.12 patch.

## Variation

Inspect per-run values before interpreting a median as stable capacity. These short local runs do not establish a confidence interval or production capacity.

| API | CCU | RPS min-max | p95 ms min-max |
|---|---:|---:|---:|
| dotnet | 100 | 2081.2-3596.2 | 85.25-98.97 |
| go | 100 | 1432.6-3576.2 | 51.32-128.19 |
| dotnet | 1000 | 4823.7-6476.6 | 249.69-390.71 |
| go | 1000 | 2174.8-4465.8 | 317.04-761.01 |
| dotnet | 5000 | 4036.0-6092.4 | 1099.72-3665.78 |
| go | 5000 | 1697.5-3410.2 | 1978.07-4892.00 |

## Every measured scenario

| API | CCU | Rep | Request/s | p50 ms | p95 ms | p99 ms | App peak | Errors | Client CPU cores |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| dotnet | 100 | 1 | 3596.2 | 18.42 | 85.25 | 101.97 | 100 | 0 | 0.70 |
| go | 100 | 1 | 3576.2 | 27.15 | 51.32 | 72.70 | 100 | 0 | 1.40 |
| go | 100 | 2 | 1432.6 | 67.14 | 128.19 | 204.63 | 100 | 0 | 0.86 |
| dotnet | 100 | 2 | 2081.2 | 29.40 | 98.97 | 171.07 | 100 | 0 | 0.69 |
| dotnet | 100 | 3 | 2673.0 | 20.55 | 92.31 | 100.96 | 100 | 0 | 0.69 |
| go | 100 | 3 | 2246.0 | 41.23 | 91.40 | 132.08 | 100 | 0 | 1.21 |
| dotnet | 1000 | 1 | 4823.7 | 192.77 | 368.07 | 472.02 | 1000 | 0 | 1.01 |
| go | 1000 | 1 | 2174.8 | 462.72 | 761.01 | 874.76 | 1000 | 0 | 1.32 |
| go | 1000 | 2 | 3007.1 | 353.71 | 472.02 | 542.58 | 1000 | 0 | 1.42 |
| dotnet | 1000 | 2 | 5221.0 | 171.07 | 390.71 | 501.06 | 1000 | 0 | 1.09 |
| dotnet | 1000 | 3 | 6476.6 | 143.02 | 249.69 | 310.79 | 1000 | 0 | 0.74 |
| go | 1000 | 3 | 4465.8 | 247.21 | 317.04 | 410.64 | 1000 | 0 | 1.46 |
| dotnet | 5000 | 1 | 6092.4 | 832.30 | 1099.72 | 1289.50 | 5000 | 0 | 0.92 |
| go | 5000 | 1 | 3298.5 | 1482.25 | 1978.07 | 2163.39 | 5000 | 0 | 1.49 |
| go | 5000 | 2 | 3410.2 | 1438.66 | 2099.76 | 2163.39 | 5000 | 0 | 1.46 |
| dotnet | 5000 | 2 | 4036.0 | 883.51 | 3665.78 | 4009.21 | 5000 | 0 | 0.88 |
| dotnet | 5000 | 3 | 5610.2 | 892.34 | 1133.04 | 1226.92 | 5000 | 0 | 1.00 |
| go | 5000 | 3 | 1697.5 | 2639.74 | 4892.00 | 5192.96 | 5000 | 0 | 1.13 |

PeakInflight counts active client calls, including pool/network queueing, not
server CPU-parallel execution. Latency includes all attempts; success-only
latency is also saved in JSON. Histogram upper bucket error is about 1%.

## Sampled target resources

Docker stats samples within measured scenario time windows, excluding warmup.
Approximately 10s sampling; timestamps precede the stats command, so boundary
classification is approximate. Memory is sampled Docker working set, not RSS
or absolute peak; CPU median is unweighted by sample interval (100% ~1 core).

| API | CCU | Samples | Observed peak MiB | Median CPU % |
|---|---:|---:|---:|---:|
| dotnet | 100 | 5 | 51.30 | 103.5 |
| go | 100 | 5 | 10.78 | 101.9 |
| dotnet | 1000 | 6 | 76.52 | 100.3 |
| go | 1000 | 5 | 48.75 | 98.7 |
| dotnet | 5000 | 6 | 181.60 | 99.0 |
| go | 5000 | 7 | 217.90 | 98.3 |

## Validation and limits

- Go: 22 table-driven unit cases PASS, including schema, nulls, malformed input, Unicode, UTF-8 and body limit. Built as part of the Docker image.
- Live API smoke: 16 cases/API PASS; additional live semantic checks: 10/API PASS.
- Postman local collection: 3 requests and 4 assertions/API PASS; no workspace publication.
- .NET API and generator Release builds succeeded with 0 warnings/errors; Compose configuration and PowerShell parser checks passed.
- All load responses are fully consumed and status-checked; semantic JSON equality is checked before load and about 1/1024 successful responses per worker, not every request.
- HTTP client timeout is 15s until response headers; body reads follow run cancellation, without a separate per-body hard deadline.
- Host Docker Desktop/WSL2 is not isolated. CPU throttling, scheduling, JIT/GC, allocation and HTTP/serializer implementations affect results.
- This compares these complete implementations and validation choices, NOT pure parser performance, fastest possible Go/.NET tuning, or a universal language ranking. No invented SLA threshold is applied.

## Reproduce

See README.md and Run-Benchmark.ps1. validation/ contains raw JSON/CSV,
metadata, diagnostics, test logs, resource samples and source hash manifest.
