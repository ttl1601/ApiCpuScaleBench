# .NET 10 CPU scaling: 1 vs 2 vs 4 CPUs

Requires Docker Desktop Linux containers and Node.js on the host.

~~~powershell
node ./Run-Benchmark.mjs
node ./Analyze-Memory.mjs
~~~

## Controlled configuration

All three services run exactly the SAME built API image and Program.cs:
.NET 10.0.13 / ASP.NET Core 10.0.13, SDK 10.0.402, Ubuntu Resolute,
Release JIT, Minimal API + Kestrel + System.Text.Json. Only CPU quota differs:
1, 2 or 4 CPUs. RAM is fixed to 1 GiB per API. Quotas are not pinned cores.
The native load generator remains a separate 4 CPU / 2 GiB .NET container.
Only one API receives load and runs during any measured scenario.

No explicit GC/heap/thread-pool/processor-count/PGO tuning, NativeAOT or source
generation. A single startup console line records actual runtime ProcessorCount,
IsServerGC and TotalAvailableMemoryBytes before serving. No extra diagnostics
endpoint or per-request logging is added. Default GC behavior may differ between
CPU counts, so this is scaling under runtime defaults, not CPU-only isolation
with GC mode fixed. Read recorded runtime/GC flags rather than assuming the mode.

Loopback ports: cpu1 18090, cpu2 18091, cpu4 18092. The API image and Docker
network are local; no cloud publication. Do not stop unrelated services/volumes.

## Workload and order

POST /parse: same 4096-byte generated UTF-8 text -> typed object -> JSON echo.
No database, cache, TLS, compression, auth or artificial delay. No custom
middleware; framework endpoint/routing middleware remains. See CONTRACT.md.

100/1000/5000 CCU, three repetitions per CPU/level: 27 measured scenarios.
Cyclic target orders: [1,2,4], [2,4,1], [4,1,2] CPUs. Every CPU configuration
appears once in each sequence position at each CCU level. This balances positions,
not all six possible pairwise permutations. Three repeats are still limited.

Every scenario starts a new target container, with other API variants stopped:
health readiness -> 5s idle baseline -> loadgen semantic baseline -> 5s warmup
-> 20s measurement -> drain + 2s cooldown -> 5s idle observation. No forced GC
or allocator trimming. Five seconds need not stabilize fresh-process tiered PGO.
No earlier result is borrowed into the same-batch CPU scaling ratios.

Before measurement, 16 functional smoke cases run against each CPU configuration.
Only this project's API containers are stopped by the runner's finally block.
Typical total duration is around 20 minutes, plus build and request drain.

## Memory and target CPU

A small shell process inside each target cgroup samples about once per second:
/proc/1/status (API process), memory.current/peak/stat/swap and cpu.stat.
The sampler is outside the PID1 RSS metric but included in cgroup CPU and memory
for all variants. Target CPU cores are usage_usec delta / elapsed seconds across
first/last samples inside the measured request window. It excludes interval edges,
not an exact full-run average. Throttle event deltas are also exported.

- baseline_rss_mib: median PID1 RSS during pre-load idle.
- load_rss_median/mean: temporal statistics in measured traffic only.
- load_rss_sampled_max: maximum one-second RSS sample during measurement;
  can miss spikes. process_hwm is OS-reported VmHWM over observed lifetime,
  including startup/warmup/idle, not load-only.
- post_idle_rss: median after loadgen exits including its drain/cooldown;
  a short idle window is not a leak test or proof memory will never return.
- cgroup working set = max(0, memory.current - inactive_file), not process RSS.
  It includes charged kernel/socket/cache memory and sampler overhead.
- lifetime cgroup peak uses last observed memory.peak before stop and includes
  startup, warmup, idle and sampler, not only measured request traffic.
- RSS counts shared resident mappings; cgroup shared-page/cache ownership differs.
  Do not sum these metrics or equate either to managed heap allocations. Image
  sharing can affect cgroup charges. Swap is recorded separately.
- Aggregate memory uses MiB, raw CSV labels original KiB/byte units.

## Reading the results

Analyze-Memory.mjs verifies runtime processor count and container CPU/RAM quotas,
scenario identity, memory coverage, errors/OOM/restarts. Raw data and medians of
three per-run values are saved in validation/. Speedup is same-CCU median RPS /
1-CPU median RPS. It is not proof of linear scaling or a production capacity SLO.

Fixed CCU is NOT fixed completed RPS, so this is not equal-throughput memory
or per-request allocation measurement. The shared Docker Desktop/WSL2 host is
not isolated; target 4 CPU plus loadgen 4 CPU can contend with host services.
Inspect achieved client/target CPU usage before assigning a scaling bottleneck.

All responses are read and status checked; baseline and about 1/1024 worker
responses are semantically checked. PeakInflight includes client/network queues,
not CPU parallelism. RPS includes drain. p95 buckets have ~1% upper-bound error.
15s HttpClient timeout ends at headers; body has cancellation but no separate
hard deadline. No arbitrary performance pass/fail threshold is invented.

Evidence: results/<batch>/manifest.json, per-scenario startup logs, pre/post
inspect, raw memory/cpu samples and request results; validation/ source hashes,
runtime/OS/host information and aggregated CSV/JSON. Older benchmark projects
are preserved and their results are not mixed into this batch.
