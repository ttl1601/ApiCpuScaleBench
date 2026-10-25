import { spawn } from 'node:child_process';
import { createWriteStream } from 'node:fs';
import * as fs from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = path.dirname(fileURLToPath(import.meta.url));
process.chdir(root);
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
const batch = new Date().toISOString().replace(/[:.]/g, '-');
const batchRoot = path.join(root, 'results', batch);
await fs.mkdir(batchRoot, { recursive: true });
await fs.mkdir('validation', { recursive: true });
async function docker(args, quiet = false) {
    const child = spawn('docker', args, { cwd: root, windowsHide: true });
    let stdout = ''; let stderr = '';
    child.stdout.on('data', data => { stdout += data; if (!quiet) process.stdout.write(data); });
    child.stderr.on('data', data => { stderr += data; if (!quiet) process.stderr.write(data); });
    const exit = await new Promise((resolve, reject) => { child.on('error', reject); child.on('close', resolve); });
    if (exit !== 0) throw new Error('docker ' + args.join(' ') + ': ' + stderr);
    return stdout;
}
const compose = (args, quiet) => docker(['compose', ...args], quiet);
async function ready(api) {
    const port = api === 'dotnet' ? 18086 : 18087;
    for (let attempt = 0; attempt < 90; attempt++) {
        try { const response = await fetch('http://localhost:' + port + '/health', { signal: AbortSignal.timeout(2000) }); if (await response.text() === 'ok') return; } catch {}
        await delay(500);
    }
    throw new Error(api + ' not ready');
}
const manifest = [];
let monitor;
try {
    await compose(['build', 'dotnet', 'rust']);
    await compose(['--profile', 'tools', 'build', 'loadgen']);
    await fs.writeFile('validation/docker-info.txt', await docker(['info'], true));
    await fs.writeFile('validation/host-containers-before.txt', await docker(['ps', '--format', '{{.Names}} {{.Image}}'], true));
    await compose(['up', '-d', 'dotnet', 'rust']);
    await compose(['run', '--rm', 'loadgen', 'smoke']);
    await fs.writeFile('validation/dotnet-runtime.txt', await compose(['exec', '-T', 'dotnet', 'dotnet', '--info'], true));
    await compose(['stop', '-t', '3', 'dotnet', 'rust']);
    for (const ccu of [100, 1000, 5000]) {
        for (let repetition = 1; repetition <= 3; repetition++) {
            const order = repetition % 2 ? ['dotnet', 'rust'] : ['rust', 'dotnet'];
            for (const api of order) {
                const scenario = { api, ccu, repetition, batch };
                const scenarioPath = path.join(batchRoot, api + '-' + ccu + '-' + repetition);
                await fs.mkdir(scenarioPath, { recursive: true });
                console.log('SCENARIO ' + api + ' CCU=' + ccu + ' REP=' + repetition);
                await compose(['up', '-d', '--force-recreate', '--no-deps', api]);
                await ready(api);
                const container = (await compose(['ps', '-q', api], true)).trim();
                await fs.writeFile(path.join(scenarioPath, 'pre-inspect.json'), await docker(['inspect', container], true));
                const sampleStream = createWriteStream(path.join(scenarioPath, 'memory.csv'));
                monitor = spawn('docker', ['exec', container, 'sh', '/monitor.sh'], { windowsHide: true });
                monitor.stdout.pipe(sampleStream);
                let monitorError = '';
                monitor.stderr.on('data', data => { monitorError += data; });
                const monitorDone = new Promise((resolve, reject) => { monitor.on('error', reject); monitor.on('close', resolve); });
                scenario.baselineStartedUtc = new Date().toISOString();
                await delay(5000);
                scenario.baselineEndedUtc = new Date().toISOString();
                const output = '/results/' + batch + '/' + api + '-' + ccu + '-' + repetition;
                const log = await compose(['run', '--rm', '-e', 'TARGET=' + api, '-e', 'SCENARIO_REPETITION=' + repetition, 'loadgen', 'run', '--ccu', '' + ccu, '--seconds', '20', '--warmup', '5', '--repetitions', '1', '--output', output]);
                await fs.writeFile(path.join(scenarioPath, 'loadgen.log'), log);
                scenario.postIdleStartedUtc = new Date().toISOString();
                await delay(5000);
                scenario.postIdleEndedUtc = new Date().toISOString();
                await compose(['stop', '-t', '3', api]);
                await monitorDone;
                monitor = null;
                await new Promise(resolve => { if (sampleStream.closed) resolve(); else sampleStream.on('close', resolve); });
                await fs.writeFile(path.join(scenarioPath, 'monitor-stderr.txt'), monitorError);
                await fs.writeFile(path.join(scenarioPath, 'post-inspect.json'), await docker(['inspect', container], true));
                scenario.path = path.relative(root, scenarioPath).replaceAll('\\', '/');
                manifest.push(scenario);
                await fs.writeFile(path.join(batchRoot, 'manifest.json'), JSON.stringify(manifest, null, 2));
            }
        }
    }
    await fs.writeFile('validation/latest-batch.txt', batch);
    await fs.writeFile('validation/host-containers-after.txt', await docker(['ps', '--format', '{{.Names}} {{.Image}}'], true));
    console.log('COMPLETE ' + batch);
} finally {
    await compose(['stop', '-t', '3', 'dotnet', 'rust']).catch(error => console.error(error.message));
    if (monitor) monitor.kill();
}
