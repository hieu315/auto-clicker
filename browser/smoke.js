const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const http = require('node:http');
const { spawn } = require('node:child_process');

async function main() {
  let clicks = 0;
  let tallClicks = 0;
  const server = http.createServer((request, response) => {
    if (request.url === '/event-tall') {
      tallClicks++;
      response.end('ok');
      return;
    }
    if (request.url === '/event') {
      clicks++;
      response.end('ok');
      return;
    }
    response.setHeader('Content-Type', 'text/html');
    if (request.url === '/tall') {
      response.end('<button style="position:absolute;left:80px;top:978px;width:80px;height:40px" onclick="fetch(\'/event-tall\',{method:\'POST\'})">Tall</button>');
      return;
    }
    response.end('<button style="position:absolute;left:80px;top:80px;width:80px;height:80px" onclick="fetch(\'/event\',{method:\'POST\'})">Click</button>');
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'swiftmacro-smoke-'));
  const configFile = path.join(root, 'config.json');
  fs.writeFileSync(configFile, JSON.stringify({
    url: `http://127.0.0.1:${server.address().port}/`,
    originX: 0, originY: 0, width: 800, height: 600,
    loopCount: 10, speed: 1, jitter: 0, profileDir: path.join(root, 'profile'),
    steps: [
      { StepId: 1, ActionType: 'LeftClick', X: 100, Y: 100, DelayBeforeMs: 0, HoldDurationMs: 0 },
      { StepId: 2, ActionType: 'WaitOnly', X: 0, Y: 0, DelayBeforeMs: 30, HoldDurationMs: 0 }
    ]
  }));
  try {
    const worker = spawn(process.execPath, [path.join(__dirname, 'headless.js'), configFile], { stdio: ['pipe', 'pipe', 'pipe'] });
    let stdout = '', stderr = '';
    worker.stdout.on('data', chunk => { stdout += chunk; });
    worker.stderr.on('data', chunk => { stderr += chunk; });
    const exit = await new Promise(resolve => worker.on('exit', resolve));
    assert.equal(exit, 0, stderr);
    assert.equal(clicks, 10, `Expected 10 clicks; got ${clicks}. Worker: ${stdout}`);
    process.stdout.write(`PASS: ${clicks} clicks in headless Chrome; ${stdout}`);

    const tallConfig = JSON.parse(fs.readFileSync(configFile, 'utf8'));
    tallConfig.url = `http://127.0.0.1:${server.address().port}/tall`;
    tallConfig.height = 720;
    tallConfig.loopCount = 1;
    tallConfig.steps = [
      { StepId: 1, ActionType: 'LeftClick', X: 100, Y: 998, DelayBeforeMs: 0, HoldDurationMs: 0 },
      { StepId: 2, ActionType: 'WaitOnly', X: 0, Y: 0, DelayBeforeMs: 50, HoldDurationMs: 0 }
    ];
    fs.writeFileSync(configFile, JSON.stringify(tallConfig));
    const tallWorker = spawn(process.execPath, [path.join(__dirname, 'headless.js'), configFile], { stdio: ['pipe', 'pipe', 'pipe'] });
    let tallError = '';
    tallWorker.stderr.on('data', chunk => { tallError += chunk; });
    const tallExit = await new Promise(resolve => tallWorker.on('exit', resolve));
    assert.equal(tallExit, 0, tallError);
    assert.equal(tallClicks, 1, `Expected click at Y=998; got ${tallClicks}`);
    process.stdout.write('PASS: viewport expands for recorded Y=998.\n');

    const infiniteConfig = JSON.parse(fs.readFileSync(configFile, 'utf8'));
    infiniteConfig.loopCount = 0;
    infiniteConfig.steps = [{ StepId: 1, ActionType: 'WaitOnly', X: 0, Y: 0, DelayBeforeMs: 5000, HoldDurationMs: 0 }];
    fs.writeFileSync(configFile, JSON.stringify(infiniteConfig));
    const stopping = spawn(process.execPath, [path.join(__dirname, 'headless.js'), configFile], { stdio: ['pipe', 'pipe', 'pipe'] });
    let stopOutput = '', stopError = '';
    stopping.stdout.on('data', chunk => { stopOutput += chunk; });
    stopping.stderr.on('data', chunk => { stopError += chunk; });
    await new Promise(resolve => setTimeout(resolve, 1000));
    stopping.stdin.write('STOP\n');
    const stopExit = await Promise.race([
      new Promise(resolve => stopping.on('exit', resolve)),
      new Promise((_, reject) => setTimeout(() => reject(new Error('Stop timed out')), 3000))
    ]);
    assert.equal(stopExit, 0, stopError);
    assert.match(stopOutput, /Đã dừng/);
    process.stdout.write('PASS: Stop cancels an infinite macro.\n');
  } finally {
    server.close();
    fs.rmSync(root, { recursive: true, force: true });
  }
}

main().catch(error => { process.stderr.write(error.stack + '\n'); process.exitCode = 1; });
