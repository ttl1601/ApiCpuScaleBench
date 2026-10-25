# .NET 10 vs Rust: JSON text API benchmark

## Scope

Two real HTTP APIs implement the same contract in CONTRACT.md:
UTF-8 JSON text -> owned string -> typed object -> serialized JSON response.
There is no database in this test. It does not reuse database benchmark results.

- ASP.NET Core / Kestrel, System.Text.Json, Release JIT (no NativeAOT or source generation).
- Rust 1.90, Axum 0.8.6, serde_json, Tokio one worker, release with thin LTO.
- Each API: Docker quota 1 CPU and memory limit 1 GiB (1,073,741,824 bytes).
- Load generator: separate .NET container, 4 CPU / 2 GiB, outside API quotas.
- This is a cgroup CPU quota, not a dedicated/pinned physical core.
- Both APIs run concurrently but receive load sequentially; the inactive API stays up.

## Run

Docker Desktop with Linux containers is required; Rust and .NET need not be installed on the host.
Cargo.lock is included. Package versions are resolved and pinned in that lockfile.

```powershell
./Run-Benchmark.ps1
./Run-Benchmark.ps1 -Ccu "5000" -Seconds 30 -Warmup 10 -Repetitions 3
```

Defaults: 100, 1000 and 5000 CCU, 20 seconds measured + 5 seconds warmup per
scenario, 2 seconds cooldown, 3 repetitions per API per CCU (18 scenarios).
The order is dotnet/rust on odd repetitions and rust/dotnet on even ones.
Three repetitions are not fully order-balanced. Typical measured duration is
about 8 minutes, plus build, smoke checks and request drain.

```powershell
docker compose --profile tools build
docker compose up -d dotnet rust
docker compose run --rm loadgen smoke
docker compose run --rm loadgen run --ccu 100,1000,5000 --seconds 20 --warmup 5 --repetitions 3
docker compose stop dotnet rust
```

The Rust demo does not install a SIGTERM handler as PID 1. Docker stop may
wait for its timeout then terminate it (exit 137); this is distinct from OOM.
Only this project's containers should be stopped. No DB containers are needed.
Results are saved immediately after each completed scenario under results/.
An interrupted scenario is not exported. Source/report ZIP omits bin/obj and
intermediate results, but includes the copied validation artifacts.

## Validation and measurements

The built-in smoke command runs 16 cases per API: round-trip, Unicode values,
malformed/empty JSON, missing fields, wrong root/type, invalid UTF-8, extra data,
null fields/elements/attribute values, unknown fields and the 1 MiB body limit.
A Postman collection supplies three reusable requests/four assertions per API:

```powershell
postman collection run postman/parse.postman_collection.json --env-var "baseUrl=http://localhost:18080" --no-report-events
postman collection run postman/parse.postman_collection.json --env-var "baseUrl=http://localhost:18081" --no-report-events
```

The collection can run locally without publishing. CLI report export via
--output may require Postman login; ordinary redirected local CLI logs do not.
The high-concurrency measurements use the included dedicated load generator,
not Postman script execution, to keep the HTTP workload identical and expose
client CPU usage. These are live local HTTP measurements, not mocked tests.

- Input payloads/input.json is exactly 4096 bytes including its final newline.
  It has 24 nested items and generated ASCII strings; it fits in memory.
- HTTP/1.1 keep-alive, no TLS/compression, zero think time, one outstanding
  request per virtual user; MaxConnectionsPerServer=10000. No pipelining.
- All responses are fully consumed and their status checked. Semantic JSON
  equality is checked in baseline tests and approximately 1/1024 successful
  responses per worker during load. Not every load response is deserialized.
- PeakInflight counts active client calls, including client pool/network queues;
  it does not count server CPU-parallel execution or prove on-wire concurrency.
- RPS = successes / actual wall time including drain after admission stops.
  p50/p95/p99 are per-run histograms (approximately 1% upper bucket error).
- AllLatency includes both failed and successful attempts; SuccessLatency is
  also exported. Errors include non-200, timeout/transport or sampled mismatch.
- HttpClient.Timeout is 15 seconds through response headers (ResponseHeadersRead).
  Body reads observe run cancellation but have no separate per-body timeout.
- CPU cores = load-generator CPU seconds / wall seconds, not target API CPU.
  Target CPU/RAM evidence comes from timestamped Docker stats and cgroup files.
- No SLA/pass threshold is invented: throughput/latency/errors are reported.

## Interpretation

Compare per-run results as well as medians. Short local tests cannot establish
production capacity or a universal ranking of C# vs Rust. HTTP stack, allocation,
serializer, JIT/PGO, CPU throttling, connection reuse and Docker Desktop/WSL2
scheduling all contribute. The host has other workloads and is not fully isolated.
No claim is made about parser-only speed, multiple CPU cores, larger payloads,
TLS, database I/O or business logic. A pure-parser microbenchmark or another
serialization mode would be a separate experiment.
