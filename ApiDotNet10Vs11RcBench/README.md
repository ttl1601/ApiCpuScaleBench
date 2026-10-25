# .NET 10 vs .NET 11 RC2: throughput and memory

Requires Docker Desktop Linux containers and Node.js on the host.
Run from this directory:

~~~powershell
node ./Run-Benchmark.mjs
node ./Analyze-Memory.mjs
~~~

## Versions and fairness

- .NET 10 API: SDK image 10.0.402-resolute, runtime 10.0.13-resolute.
- .NET 11 API: SDK image 11.0.100-rc.2-resolute, runtime 11.0.0-rc.2-resolute.
- Both runtime/SDK variants use Ubuntu Resolute rather than different base OSes.
  Runtime --info, /etc/os-release and image IDs are saved as verification evidence.
- Identical Program.cs handler files, built for net10.0 and net11.0 respectively.
  Minimal API / Kestrel / System.Text.Json, Release JIT, runtime defaults.
  This tests SDK/compiler + runtime + ASP.NET + serializer versions as a stack;
  it does not isolate a JIT-only or GC-only change. No NativeAOT/source generation.
- The load generator stays on .NET 10 for both targets, 4 CPU / 2 GiB.
- Each API has 1 CPU quota / 1 GiB RAM, not a dedicated or pinned core.
- Host loopback ports: .NET 10 18088, .NET 11 RC2 18089. No publication.

Official release metadata JSON is saved in validation/ for 10.0 and 11.0.
Exact resolved runtime patch/build versions are in validation/*-runtime.txt.
Tag versions are explicit but can be republished; inspect records actual image IDs.
The RC is prerelease, not .NET 11 general availability.

## Workload

POST /parse: 4096-byte generated UTF-8 text -> typed object -> JSON echo.
Same contract, validation and source code on both sides. No database, auth, TLS,
compression, cache, artificial delay, custom middleware or request logging.
Framework endpoint/routing machinery remains. See CONTRACT.md and payloads/.

The runner validates 16 smoke cases per API, then measures closed-loop
100/1000/5000 CCU with no think time. Each level has three repetitions per target
(18 measured scenarios), with order reversed on even repetitions. Odd total
repetitions mean target order is not completely balanced.

Each measured scenario force-recreates ONLY its own target container. The other
API is stopped. This avoids retained heap/socket memory carrying over across
CCU levels. Only this project's containers are stopped; unrelated services remain.

Phases: health readiness -> 5s idle baseline -> loadgen semantic baseline check
-> 5s warmup -> 20s measurement -> drain + 2s cooldown -> 5s idle observation.
No forced GC, allocator trim or explicit heap/GC/PGO tuning. Five seconds warmup
may not stabilize tiered JIT/PGO; these are short, fresh-process measurements.
Both apps normally exit gracefully; OOMKilled/restarts are recorded separately.

## Memory

A small shell process in each target cgroup reads /proc/1/status and cgroup v2
approximately once per second. PID1 RSS excludes the sampler; cgroup totals
include the sampler and charged kernel/cache/socket memory. Its overhead affects
both API CPU quotas. There are no diagnostic endpoints in the measured API.

- baseline_rss_mib: median PID1 RSS over pre-load idle.
- load_rss_median_mib / load_rss_mean_mib: temporal statistics in the actual
  measured request start/end interval, not warmup or cooldown.
- load_rss_sampled_max_mib: maximum one-second RSS sample during measurement;
  short spikes can be missed. process_hwm_mib is OS-reported VmHWM over the
  observed process lifetime, including warmup and idle, not a load-only peak.
- post_idle_rss_mib: median over post-load idle; not a memory-leak test.
- load_cgroup_current_sampled_max_mib: sampled max memory.current during load.
- working_set: max(0, memory.current - inactive_file). This is not process RSS.
- cgroup_lifetime_peak_mib: last observed memory.peak before stop, including
  startup, warmup, measurement, idle and sampler overhead. Not load-only.
- RSS includes shared resident mappings; cgroup shared-page accounting differs.
  A cgroup metric may be smaller than process RSS. Never add the two metrics or
  treat either as managed heap allocation size. Shared cache ownership and
  image-layer sharing affect whole-container comparison on a shared host.
- KiB/bytes preserved in raw memory.csv; all aggregate memory values are MiB.
  Swap is recorded, not implicitly counted as physical RAM.

## Throughput and interpretation

Equal CCU is not equal completed RPS. This is not equal-throughput memory or
per-request allocation profiling. RPS = successes / actual wall time including
drain. p95 uses approximately 1% histogram upper-bound buckets. Every response
is fully read and status checked; semantic equality checked at baseline and
approximately 1/1024 successful responses per worker, not every request.
PeakInflight counts active client calls including queues, not CPU parallelism.
15s HttpClient timeout ends at response headers; body has cancellation but no
separate hard deadline. The host is shared Docker Desktop/WSL2, not isolated.

Analyze-Memory.mjs verifies quotas, actual repetition, sample coverage and
OOM/restarts/errors. Exports per-run CSV/JSON and medians of the three per-run
values, not pooled request percentiles. Read ranges alongside medians. No
performance SLA was supplied, so no artificial performance threshold is used.
Do not mix numbers from previous .NET/Rust/C++ runs into this version comparison.

Evidence: results/<batch>/manifest.json and scenario subfolders; validation/.
