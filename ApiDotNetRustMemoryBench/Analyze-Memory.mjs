import * as fs from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = path.dirname(fileURLToPath(import.meta.url));
const batch = (await fs.readFile(path.join(root, 'validation/latest-batch.txt'), 'utf8')).trim();
const scenarios = JSON.parse(await fs.readFile(path.join(root, 'results', batch, 'manifest.json'), 'utf8'));
const median = values => { const sorted = [...values].sort((left, right) => left - right); const middle = Math.floor(sorted.length / 2); return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2; };
const maximum = values => Math.max(...values);
const mean = values => values.reduce((sum, value) => sum + value, 0) / values.length;
const rows = [];
for (const scenario of scenarios) {
    const folder = path.join(root, scenario.path);
    const runDirs = (await fs.readdir(folder, { withFileTypes: true })).filter(entry => entry.isDirectory() && entry.name.endsWith('-run'));
    if (runDirs.length !== 1) throw new Error('Expected one load run: ' + folder);
    const runs = JSON.parse(await fs.readFile(path.join(folder, runDirs[0].name, 'results.json'), 'utf8'));
    if (runs.length !== 1) throw new Error('Expected one measured scenario');
    const run = runs[0];
    if (run.Api !== scenario.api || run.Ccu !== scenario.ccu || run.Repetition !== scenario.repetition) throw new Error('Scenario metadata mismatch');
    const lines = (await fs.readFile(path.join(folder, 'memory.csv'), 'utf8')).trim().split(/\r?\n/);
    const headers = lines.shift().split(',');
    const samples = lines.map(line => Object.fromEntries(line.split(',').map((value, index) => [headers[index], Number(value)])));
    if (samples.some(sample => Object.keys(sample).length !== headers.length || !Object.values(sample).every(Number.isFinite))) throw new Error('Invalid memory CSV');
    const during = (start, end) => samples.filter(sample => sample.epoch_ms >= Date.parse(start) && sample.epoch_ms <= Date.parse(end));
    const baseline = during(scenario.baselineStartedUtc, scenario.baselineEndedUtc);
    const load = during(run.StartedUtc, run.EndedUtc);
    const idle = during(scenario.postIdleStartedUtc, scenario.postIdleEndedUtc);
    if (baseline.length < 3 || load.length < 10 || idle.length < 3) throw new Error('Insufficient memory samples: ' + folder);
    const inspect = JSON.parse(await fs.readFile(path.join(folder, 'post-inspect.json'), 'utf8'))[0];
    const pre = JSON.parse(await fs.readFile(path.join(folder, 'pre-inspect.json'), 'utf8'))[0];
    if (pre.HostConfig.NanoCpus !== 1e9 || pre.HostConfig.Memory !== 1073741824) throw new Error('Wrong quotas');
    const row = {
        api: scenario.api, ccu: scenario.ccu, repetition: scenario.repetition,
        baseline_rss_mib: median(baseline.map(sample => sample.rss_kib / 1024)),
        load_rss_mean_mib: mean(load.map(sample => sample.rss_kib / 1024)),
        load_rss_median_mib: median(load.map(sample => sample.rss_kib / 1024)),
        load_rss_sampled_max_mib: maximum(load.map(sample => sample.rss_kib / 1024)),
        process_hwm_mib: maximum(samples.map(sample => sample.hwm_kib / 1024)),
        post_idle_rss_mib: median(idle.map(sample => sample.rss_kib / 1024)),
        load_cgroup_current_sampled_max_mib: maximum(load.map(sample => sample.cgroup_current_bytes / 1048576)),
        load_cgroup_working_set_sampled_max_mib: maximum(load.map(sample => Math.max(0, sample.cgroup_current_bytes - sample.inactive_file_bytes) / 1048576)),
        cgroup_lifetime_peak_mib: maximum(samples.map(sample => sample.cgroup_peak_bytes / 1048576)),
        max_swap_mib: maximum(samples.map(sample => sample.swap_bytes / 1048576)),
        load_samples: load.length, baseline_samples: baseline.length, post_idle_samples: idle.length,
        success_rps: run.SuccessRps, p95_ms: run.AllLatency.P95Ms, errors: run.Errors, success: run.Success,
        peak_inflight: run.PeakInflight, client_cpu_cores: run.ClientCpuCores,
        oom_killed: inspect.State.OOMKilled, restart_count: inspect.RestartCount, exit_code: inspect.State.ExitCode,
        path: scenario.path
    };
    rows.push(row);
}
const summaries = [];
for (const ccu of [100, 1000, 5000]) for (const api of ['dotnet', 'rust']) {
    const group = rows.filter(row => row.api === api && row.ccu === ccu);
    if (group.length !== 3) throw new Error('Missing repetitions');
    const summary = { api, ccu };
    for (const field of ['baseline_rss_mib', 'load_rss_mean_mib', 'load_rss_median_mib', 'load_rss_sampled_max_mib', 'process_hwm_mib', 'post_idle_rss_mib', 'load_cgroup_current_sampled_max_mib', 'load_cgroup_working_set_sampled_max_mib', 'cgroup_lifetime_peak_mib', 'success_rps', 'p95_ms']) summary[field] = median(group.map(row => row[field]));
    summary.rss_max_range = [Math.min(...group.map(row => row.load_rss_sampled_max_mib)), Math.max(...group.map(row => row.load_rss_sampled_max_mib))];
    summaries.push(summary);
}
await fs.writeFile(path.join(root, 'validation/memory-results.json'), JSON.stringify(rows, null, 2));
await fs.writeFile(path.join(root, 'validation/memory-summary.json'), JSON.stringify(summaries, null, 2));
const csvHeaders = Object.keys(rows[0]);
await fs.writeFile(path.join(root, 'validation/memory-results.csv'), csvHeaders.join(',') + '\n' + rows.map(row => csvHeaders.map(field => row[field]).join(',')).join('\n') + '\n');
console.log(JSON.stringify({ batch, scenarios: rows.length, success: rows.reduce((sum, row) => sum + row.success, 0), errors: rows.reduce((sum, row) => sum + row.errors, 0), summaries }, null, 2));
if (rows.some(row => row.errors || row.oom_killed || row.restart_count)) process.exitCode = 2;
