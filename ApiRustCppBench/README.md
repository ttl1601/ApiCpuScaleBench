# Rust vs C++ JSON HTTP benchmark

Local Docker benchmark; each server has a 1 CPU quota and 1 GiB RAM limit.
Load generator has 4 CPU / 2 GiB. Ports: C++ 18084, Rust 18085, loopback only.

## Run

From this directory in PowerShell with Docker Desktop Linux containers:

~~~powershell
./Run-Benchmark.ps1
~~~

Default: 100, 1000, 5000 closed-loop concurrent clients, no think time;
20 seconds measured + 5 seconds warmup + 2 seconds cooldown per scenario;
3 repetitions per implementation/level (18 measured scenarios).
Targets run sequentially; even repetitions reverse their order. Three
repetitions do not completely balance target order.
The script builds, checks 16 functional cases per server, records resources,
runs measurements, and stops ONLY this project's servers in finally.

POST /parse: 4096 UTF-8 bytes -> owned text -> typed Payload -> JSON echo.
See CONTRACT.md. C++ uses Drogon 1.9.0, nlohmann/json 3.11.3, GCC 14.2,
C++20, Release -O3 -flto. Rust uses Axum/serde_json, release thin LTO,
one Tokio worker; Cargo.lock pins dependencies. Each framework may also
use auxiliary threads. C++ deliberately uses DOM -> typed object -> DOM;
Rust serde deserializes directly to typed objects. This is a comparison
of these implementations, NOT an isolated language or parser ranking.
Drogon's linked JsonCpp dependency is not the benchmark JSON parser.
Neither handler calls a database, cache, TLS, authentication, or compression.

## Measurement caveats

- All response bodies read and HTTP status checked. Semantic echo verified
  before load and about every 1024th response per worker, not every response.
- RPS counts successful requests / actual wall time including drain.
- Latency includes client queueing, network, body transfer; histogram bucket
  upper bounds have about 1% quantization error.
- PeakInflight measures active client calls, not server CPU parallelism.
- HttpClient 15-second timeout ends at headers (ResponseHeadersRead);
  there is no separate response-body deadline. This is not a hard-deadline test.
- Resource samples every roughly 5-10 seconds are Docker working set,
  not RSS or guaranteed peak. CPU quota is not a pinned or dedicated core.
- Host and unrelated workloads are not isolated. Short runs and three
  repetitions cannot establish production capacity or statistical confidence.
- Rust PID1 has no SIGTERM handler; Docker stop may end in exit 137 after
  its grace period. Inspect OOMKilled separately; shutdown is outside measurement.
- Debian apt packages and base image tags may change on future rebuilds.
  Exact measured package versions, image IDs and source hashes are in validation/.
- No performance SLA was supplied; report metrics without an invented pass gate.

Results: results/<UTC>-run/{results.csv,results.json,metadata.json}.
Validation evidence: validation/. Postman collection: postman/.

Functional Postman runs (Postman CLI installed; no workspace upload required):

~~~powershell
postman collection run postman/parse.postman_collection.json --env-var 'baseUrl=http://localhost:18084' --no-report-events
postman collection run postman/parse.postman_collection.json --env-var 'baseUrl=http://localhost:18085' --no-report-events
~~~
