# Go vs .NET 10 - JSON text API benchmark

This is a NEW paired run, not Go numbers compared with old .NET measurements.
The earlier Rust/.NET and database artifacts remain unchanged.

## Implementations

- .NET: ASP.NET Core Minimal API on Kestrel, System.Text.Json, Release JIT,
  no NativeAOT or source generation. No custom middleware, auth, compression,
  CORS, rate limiting or per-request logging; framework routing still exists.
- Go 1.27.3: standard net/http ServeMux + encoding/json, no third-party router
  or JSON package, no custom middleware. CGO_ENABLED=0, normal optimized build.
- Both: POST /parse accepts UTF-8 JSON text -> owned string -> typed object ->
  serialized JSON response. No DB, cache, artificial delay, TLS or compression.
- All properties are required/non-null and unknown fields are rejected.
  Go uses pointer fields to distinguish missing/null from valid zero/false.
  .NET uses required properties and explicit reference validation.
- For this comparison BOTH accept case-insensitive ASCII property names.
  .NET explicitly sets PropertyNameCaseInsensitive=true to match encoding/json.
  This differs from the previous Rust comparison, so old .NET RPS is not reused.
- Go's JSON encoder appends a newline; JSON equivalence, not byte-identical
  response serialization, is required. See CONTRACT.md for scope/exclusions.

## Resource limits and workload

- Each API: Docker quota 1 CPU and memory limit 1 GiB (1073741824 bytes).
- Go GOMAXPROCS=1; this does not limit goroutine count. Normal GC defaults.
- Load generator: separate .NET container, 4 CPU / 2 GiB, outside API quotas.
- CPU quotas are not dedicated/pinned physical cores. The host is not isolated.
- Exact same 4096-byte payload (including newline) as the previous API test:
  ASCII text, 24 nested items, strings, booleans, int32 and finite doubles.
- 100 / 1000 / 5000 CCU; 20 seconds measured + 5 seconds warmup per scenario;
  2 seconds cooldown; 3 repetitions per API/CCU = 18 measured scenarios.
- APIs receive load sequentially; odd repetition dotnet/go, even go/dotnet.
  Three repetitions do not fully balance execution order.
- HTTP/1.1 exact with keep-alive, closed-loop zero think time, one outstanding
  request per user; MaxConnectionsPerServer=10000. No HTTP pipelining.

## Run with Docker Desktop (Linux containers)

No host Go or .NET installation is required. There are no Go module dependencies.

```powershell
./Run-Benchmark.ps1
./Run-Benchmark.ps1 -Ccu "5000" -Seconds 30 -Warmup 10 -Repetitions 3
```

Equivalent commands:

```powershell
docker compose --profile tools build
docker compose up -d dotnet go
docker compose run --rm loadgen smoke
docker compose run --rm loadgen run --ccu 100,1000,5000 --seconds 20 --warmup 5 --repetitions 3
docker compose stop dotnet go
```

Host ports: .NET 127.0.0.1:18082; Go 127.0.0.1:18083. Do not stop unrelated
containers. Go handles SIGTERM for graceful shutdown. Completed scenario JSON
and CSV are saved immediately under results/. An interrupted scenario is omitted.

## Verification

Go Docker build runs go test -v ./... with 22 table-driven cases: success,
Unicode, case-insensitivity, false, required/null/type/unknown/nested
validation, int32 overflow, malformed/trailing data, UTF-8 and body-size limit.
The loadgen smoke command runs 16 live cases/API. Additional local HTTP tests
cover raw UTF-8, application/json and text/plain, mixed-case names, empty
containers, zero/false, and required/null/nested failures (10 cases/API).

Reusable Postman collection (3 requests, 4 assertions/API):

```powershell
postman collection run postman/parse.postman_collection.json --env-var "baseUrl=http://localhost:18082" --no-report-events
postman collection run postman/parse.postman_collection.json --env-var "baseUrl=http://localhost:18083" --no-report-events
```

These are live local tests, not mocked. No workspace publication is needed.
Performance numbers come from the dedicated identical loadgen, not Postman
script execution. Postman collection examples need not be exactly 4 KiB;
the measured workload always uses payloads/input.json and records its SHA256.

## Reading the results

All responses are fully read and status-checked; semantic equality is verified
in functional checks and about 1/1024 successful responses per worker during load.
Not every response is deserialized by the client. RPS = successes / actual wall
time including drain. AllLatency includes failures; SuccessLatency is also saved.
Histograms have approximately 1% upper-bucket error. PeakInflight counts active
client calls including pool/network queues, not simultaneous server CPU work.

The client timeout is 15 seconds through response headers; body reads follow
run cancellation but have no separate hard deadline. ClientCpuCores measures
the generator, not target API CPU. Timestamped Docker stats/cgroup diagnostics
provide target CPU/RAM evidence; sampled working set is not absolute peak RSS.

Report per-run values and medians. Short local runs cannot establish production
capacity, pure parser performance or a universal Go/C# language ranking. Payload,
validation rules, serializer/HTTP stack, allocation, GC, JIT, CPU throttling and
host scheduling all contribute. No SLA threshold is invented for this comparison.
