# .NET 10 vs Rust: memory benchmark

Run on Docker Desktop Linux containers with Node.js installed on the host:

~~~powershell
node ./Run-Benchmark.mjs
node ./Analyze-Memory.mjs
~~~

The runner builds the existing .NET Minimal API and Rust Axum implementations,
executes 16 smoke cases per API, then runs 18 measured scenarios. Each API has
1 CPU quota / 1 GiB RAM; the dedicated native load generator has 4 CPU / 2 GiB.
Host loopback ports: .NET 18086 and Rust 18087. No database or external traffic.

## Experiment

- POST /parse uses the same 4096-byte UTF-8 JSON text payload and typed echo
  contract as the earlier .NET/Rust benchmark. No changes to application handlers.
- 100/1000/5000 closed-loop concurrent clients, 20 seconds measured after
  5 seconds warmup, three repetitions per API/level. Even repetitions reverse
  target order. Three repetitions are not fully order-balanced.
- Each scenario force-recreates ONLY its own target container. The other target
  is stopped, so retained allocations from previous CCU levels do not carry over.
- A health request initializes routing before the 5-second pre-load idle baseline.
  The runner then starts loadgen (including its baseline semantic echo validation).
  After loadgen exits (including 2-second cooldown), it records 5 seconds idle.
- No forced GC, malloc trim, memory tuning, NativeAOT, or diagnostics endpoint.
  .NET Release JIT defaults, Rust Release thin LTO, one Tokio worker.
- Memory sampled about once per second by a small shell process executed inside
  the target cgroup. RSS reads /proc/1/status, i.e. API PID1, not the shell.
  The sampler's small cost is included in cgroup memory and CPU quota for both APIs.
- The runner stops only its project containers in finally. Rust lacks a PID1
  SIGTERM handler, so its final exit 137 after the 3-second grace period is
  expected shutdown, not proof of OOM. Inspect OOMKilled and restart_count.

## Memory metrics

- baseline_rss_mib: median PID1 RSS during the pre-load idle window.
- load_rss_median_mib / load_rss_mean_mib: temporal RSS statistics within
  actual measured request start/end timestamps, excluding warmup and cooldown.
- load_rss_sampled_max_mib: maximum sampled PID1 RSS in that measured window;
  one-second sampling can miss spikes. process_hwm_mib is OS-reported VmHWM
  over the observed process lifetime, including startup/warmup, not load-only.
- post_idle_rss_mib: median RSS in the post-load idle window; this does not prove
  a leak or guarantee the process will/will not release memory later.
- cgroup_current includes charged process/kernel/cache memory for the target
  and sampler. working_set is max(0, memory.current - inactive_file), matching
  the Linux Docker-style cache subtraction, not identical to process RSS.
- cgroup_lifetime_peak_mib uses memory.peak, last observed before shutdown,
  including startup, baseline, warmup, measurement, idle and sampler overhead.
- RSS counts resident shared mappings for this process, whereas cgroup charges
  shared pages according to ownership rules. Never sum RSS and cgroup memory or
  compare their numbers as if the same metric. Neither is managed heap size.
- All values are MiB (1024*1024 bytes), not decimal MB. memory.csv preserves
  original byte / KiB units in column names. Swap is recorded separately.

Analyze-Memory.mjs verifies per-scenario quota, metadata, sample coverage,
OOM/restarts/errors and exports per-run CSV/JSON plus summary JSON to validation/.
Summary metrics are medians of the three per-run values, not a pooled percentile.

## Limits

The experiment holds concurrent clients constant, NOT completed RPS. Different
throughput means different work completed: do not call this equal-throughput RAM
or per-request allocation measurement. Fresh .NET processes are JIT warming up;
five seconds does not guarantee fully stable tiered-PGO state. This is short-run
local evidence on a shared Docker Desktop/WSL2 host, not production capacity.
No memory/latency SLA was supplied, so no artificial performance pass gate is used.

Loadgen consumes every response and checks status, with semantic equality at
baseline and approximately 1/1024 responses per worker during load. PeakInflight
is active client requests including queues, not CPU-parallel server work. RPS uses
wall time including drain. Histograms use about 1% bucket quantization. A 15-second
HttpClient timeout covers response headers only; body reads have cancellation
but no separate deadline. There is no TLS, compression, cache or persistence.

The APIs use .NET 10 Minimal API + System.Text.Json vs Rust Axum + serde_json.
Results describe these implementations, not all .NET/Rust software. Preserve
results from previous benchmark projects; do not mix their RAM measurements
with this experiment's PID1 RSS numbers.
