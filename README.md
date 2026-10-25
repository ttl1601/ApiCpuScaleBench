# API CPU Scale Bench

Reproducible local Docker benchmark tools for .NET APIs, Rust, Go, C++, and
primary-key reads across PostgreSQL, MongoDB, Cassandra, ScyllaDB and Redis.

## Benchmark suites

| Directory | Comparison | Entry point |
| --- | --- | --- |
| [ApiDotNet10CpuScaleBench](ApiDotNet10CpuScaleBench/README.md) | .NET 10: 1 / 2 / 4 CPU, fixed 1 GiB RAM | node Run-Benchmark.mjs |
| [ApiDotNet10Vs11RcBench](ApiDotNet10Vs11RcBench/README.md) | .NET 10 vs .NET 11 RC2: throughput and RAM | node Run-Benchmark.mjs |
| [ApiDotNetRustMemoryBench](ApiDotNetRustMemoryBench/README.md) | .NET 10 vs Rust: API RSS and container memory | node Run-Benchmark.mjs |
| [ApiJsonBench](ApiJsonBench/README.md) | .NET 10 vs Rust: JSON HTTP API | ./Run-Benchmark.ps1 |
| [ApiGoDotNetBench](ApiGoDotNetBench/README.md) | Go vs .NET 10: JSON HTTP API | ./Run-Benchmark.ps1 |
| [ApiRustCppBench](ApiRustCppBench/README.md) | Rust vs C++: JSON HTTP API | ./Run-Benchmark.ps1 |
| [DbReadBench](DbReadBench/README.md) | PostgreSQL / MongoDB 8 / Cassandra / ScyllaDB / Redis reads | See suite README for database selection |

Run commands from the corresponding suite directory. The suites have different
profiles and defaults: inspect that suite's README and compose.yaml first.
They are independent projects, not a single command that starts every database.

## Requirements

- Docker Desktop in Linux-container mode (or compatible Docker Engine + Compose).
- Node.js for the three .mjs-based memory/CPU suites; PowerShell for .ps1 runners.
- API/database compilers and runtimes are built inside Docker. Host installations
  of Rust, Go, C++ or .NET are not required for Docker-based execution.
- Enough available host CPU/RAM for the selected API/database and load generator.
  Run suites one at a time to avoid unintended cross-test resource contention.

## Example: .NET 10 CPU scaling

~~~powershell
cd ApiDotNet10CpuScaleBench
node ./Run-Benchmark.mjs
node ./Analyze-Memory.mjs
~~~

Defaults: API quotas 1 / 2 / 4 CPU, 1 GiB RAM each; 100 / 1000 / 5000 concurrent
clients, three repetitions per configuration/level. Each measured target starts
in a fresh container, with the other variants stopped. The separate load generator
has a 4 CPU / 2 GiB limit. See the suite README for timings and GC-mode diagnostics.

The API workload is POST JSON text -> typed object -> JSON response, without a
database. Other suites include their own payloads, source, Dockerfiles, Compose
configuration, load generators, functional smoke tests and Postman collections.

## Published measurements

Local measurements were collected on October 24-25, 2026. Each API suite has a
RESULTS.md; database reports include the separate MongoDB, Cassandra and Redis
comparisons. Curated numeric CSV/JSON exports are under validation*/.

- [CPU scaling results](ApiDotNet10CpuScaleBench/RESULTS.md)
- [.NET 10 vs 11 RC2 results](ApiDotNet10Vs11RcBench/RESULTS.md)
- [.NET vs Rust RAM results](ApiDotNetRustMemoryBench/RESULTS.md)
- [.NET vs Rust HTTP results](ApiJsonBench/RESULTS.md)
- [Go vs .NET HTTP results](ApiGoDotNetBench/RESULTS.md)
- [Rust vs C++ HTTP results](ApiRustCppBench/RESULTS.md)
- [Redis vs ScyllaDB results](DbReadBench/REDIS-SCYLLA-5K-RESULTS.md)
- [Cassandra vs ScyllaDB 5K inflight results](DbReadBench/CASSANDRA-SCYLLA-5K-INFLIGHT-RESULTS.md)

Build outputs, runtime results/ folders, ZIP archives, raw resource streams,
Docker inspect, machine diagnostics and local console logs are intentionally not
published. Historical reports may reference those original local evidence paths;
this repository contains the curated aggregate exports, not the complete original
machine-level archives. Rerunning generates new raw evidence locally. The memory
analyzer needs a completed local run; published aggregate CSV/JSON can be inspected
without rerunning. Source-hash files describe the original measured source bytes.

## Interpretation and safety

These are short local measurements on a shared Docker Desktop/WSL2 host, not a
universal language/database ranking or production capacity guarantee. Compare
same-batch results and run ranges; do not combine unrelated batches into a league
table. CPU quotas are not dedicated physical cores, CCU is not fixed RPS, and
process RSS differs from container working set. Default GC mode can change with
CPU count. See each report for validation, error counts and measurement caveats.

Services bind host loopback ports. Database credentials such as bench-local-only
are intentionally public, disposable benchmark defaults, not production secrets.
Do not reuse these credentials or expose the containers publicly. Review ports
before starting a suite. Do not remove unrelated containers or database volumes.
