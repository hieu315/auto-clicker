// Runs the existing macro in an isolated, invisible Chrome profile using CDP.
// Requires Node.js 22+ and a local Chrome installation; no npm packages.
const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');

const config = JSON.parse(fs.readFileSync(process.argv[2], 'utf8').replace(/^\uFEFF/, ''));
const chromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
// A recorded screen coordinate can be below the default 720px viewport.
// Grow the hidden viewport to fit the macro; this does not change its delays.
for (const step of config.steps) {
  if (/^(Mouse|Left|Right|Middle)/.test(step.ActionType)) {
    config.width = Math.max(config.width, step.X - config.originX + 1);
    config.height = Math.max(config.height, step.Y - config.originY + 1);
  }
}
let stopped = false;
process.stdin.setEncoding('utf8');
process.stdin.on('data', value => { if (value.includes('STOP')) stopped = true; });
process.stdin.on('end', () => { stopped = true; });

const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
async function wait(ms) {
  if (ms <= 0) return !stopped;
  const end = performance.now() + ms;
  while (!stopped && performance.now() < end) await pause(Math.min(20, Math.max(1, end - performance.now())));
  return !stopped;
}

async function getPage(port) {
  const response = await fetch(`http://127.0.0.1:${port}/json/list`);
  if (!response.ok) throw new Error(`Chrome DevTools: HTTP ${response.status}`);
  const pages = await response.json();
  const page = pages.find(item => item.type === 'page');
  if (!page) throw new Error('Không tìm thấy tab Chrome ẩn.');
  return page.webSocketDebuggerUrl;
}

function connect(url) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(url);
    const pending = new Map();
    let nextId = 0;
    ws.addEventListener('open', () => resolve({
      async send(method, params = {}) {
        const id = ++nextId;
        return new Promise((ok, fail) => {
          pending.set(id, { ok, fail });
          ws.send(JSON.stringify({ id, method, params }));
        });
      },
      close() { ws.close(); }
    }));
    ws.addEventListener('message', event => {
      const data = JSON.parse(event.data);
      if (!data.id || !pending.has(data.id)) return;
      const item = pending.get(data.id);
      pending.delete(data.id);
      if (data.error) item.fail(new Error(data.error.message));
      else item.ok(data.result || {});
    });
    ws.addEventListener('error', reject);
    ws.addEventListener('close', () => {
      for (const item of pending.values()) item.fail(new Error('Chrome ngắt kết nối.'));
      pending.clear();
    });
  });
}

function coordinates(step) {
  const x = step.X - config.originX;
  const y = step.Y - config.originY;
  if (x < 0 || y < 0 || x >= config.width || y >= config.height) {
    throw new Error(`Bước #${step.StepId}: tọa độ (${x}, ${y}) nằm ngoài vùng web. Kiểm tra Gốc web X/Y.`);
  }
  return { x, y };
}

function keyName(vk) {
  const special = { 8: 'Backspace', 9: 'Tab', 13: 'Enter', 16: 'Shift', 17: 'Control', 18: 'Alt', 27: 'Escape', 32: ' ', 37: 'ArrowLeft', 38: 'ArrowUp', 39: 'ArrowRight', 40: 'ArrowDown', 46: 'Delete' };
  return special[vk] || (vk >= 48 && vk <= 90 ? String.fromCharCode(vk) : 'Unidentified');
}

function keyCode(vk) {
  if (vk >= 65 && vk <= 90) return `Key${String.fromCharCode(vk)}`;
  if (vk >= 48 && vk <= 57) return `Digit${String.fromCharCode(vk)}`;
  return keyName(vk);
}

async function runStep(cdp, step) {
  const type = step.ActionType;
  const hold = Math.max(0, Math.round((step.HoldDurationMs || 0) / config.speed));
  const mouseButton = type.startsWith('Right') ? 'right' : type.startsWith('Middle') ? 'middle' : 'left';
  const buttonCode = mouseButton === 'left' ? 1 : mouseButton === 'right' ? 2 : 4;
  const mouse = async (eventType, button, buttons) => cdp.send('Input.dispatchMouseEvent', {
    type: eventType, ...coordinates(step), button, buttons, clickCount: eventType === 'mouseMoved' ? 0 : 1
  });
  const keyboard = async isUp => {
    const vk = step.VirtualKeyCode;
    const text = vk >= 65 && vk <= 90 ? String.fromCharCode(vk).toLowerCase()
      : vk >= 48 && vk <= 57 ? String.fromCharCode(vk)
      : vk === 32 ? ' ' : undefined;
    await cdp.send('Input.dispatchKeyEvent', {
      type: isUp ? 'keyUp' : 'keyDown', key: keyName(vk), code: keyCode(vk),
      ...(!isUp && text ? { text } : {}),
      windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk
    });
  };
  if (type === 'WaitOnly') return;
  if (type === 'MouseMove') return mouse('mouseMoved', 'none', 0);
  if (type === 'MouseWheel') return cdp.send('Input.dispatchMouseEvent', {
    type: 'mouseWheel', ...coordinates(step), deltaX: 0, deltaY: -step.MouseData, button: 'none', buttons: 0
  });
  if (type === 'KeyDown') return keyboard(false);
  if (type === 'KeyUp') return keyboard(true);
  if (type === 'KeyPress') { await keyboard(false); if (await wait(hold)) await keyboard(true); return; }
  if (type.endsWith('Down')) return mouse('mousePressed', mouseButton, buttonCode);
  if (type.endsWith('Up')) return mouse('mouseReleased', mouseButton, 0);
  if (type.endsWith('Click')) {
    await mouse('mousePressed', mouseButton, buttonCode);
    if (await wait(hold)) await mouse('mouseReleased', mouseButton, 0);
    return;
  }
  throw new Error(`Loại bước không hỗ trợ: ${type}`);
}

async function main() {
  if (!fs.existsSync(chromePath)) throw new Error(`Không tìm thấy Chrome: ${chromePath}`);
  fs.mkdirSync(config.profileDir, { recursive: true });
  const portFile = path.join(config.profileDir, 'DevToolsActivePort');
  try { fs.unlinkSync(portFile); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  const child = spawn(chromePath, [
    '--headless=new', '--remote-debugging-port=0', `--user-data-dir=${config.profileDir}`,
    '--no-first-run', '--no-default-browser-check',
    `--window-size=${config.width},${config.height}`, 'about:blank'
  ], { stdio: 'ignore', windowsHide: true });
  let launchError;
  child.on('error', error => { launchError = error; });
  let cdp;
  try {
    let port;
    for (let i = 0; i < 200 && !stopped; i++) {
      if (fs.existsSync(portFile)) {
        const text = fs.readFileSync(portFile, 'utf8').trim();
        const found = Number(text.split(/\r?\n/)[0]);
        if (found > 0) { port = found; break; }
      }
      if (launchError) throw launchError;
      if (child.exitCode !== null) throw new Error('Chrome không khởi động. Hãy đóng cửa sổ Chrome đăng nhập của tool rồi thử lại.');
      await pause(50);
    }
    if (stopped) return 'Đã dừng Chrome ẩn.';
    if (!port) throw new Error('Không kết nối được Chrome ẩn. Hãy đóng cửa sổ Chrome đăng nhập của tool rồi thử lại.');
    cdp = await connect(await getPage(port));
    await cdp.send('Page.enable');
    await cdp.send('Emulation.setDeviceMetricsOverride', {
      width: config.width, height: config.height, deviceScaleFactor: 1, mobile: false
    });
    const navigation = await cdp.send('Page.navigate', { url: config.url });
    if (navigation.errorText) throw new Error(`Không mở được URL: ${navigation.errorText}`);
    for (let i = 0; i < 300 && !stopped; i++) {
      const ready = await cdp.send('Runtime.evaluate', { expression: 'document.readyState', returnByValue: true });
      if (ready.result && ready.result.value === 'complete') break;
      await pause(50);
    }
    for (let loop = 0; (config.loopCount === 0 || loop < config.loopCount) && !stopped; loop++) {
      for (const step of config.steps) {
        if (stopped) break;
        const scaled = Math.round(step.DelayBeforeMs / config.speed);
        const jitter = config.jitter ? Math.floor(Math.random() * (config.jitter * 2 + 1)) - config.jitter : 0;
        if (!await wait(Math.max(0, scaled + jitter))) break;
        await runStep(cdp, step);
      }
    }
    return stopped ? 'Đã dừng Chrome ẩn.' : 'Chrome ẩn đã hoàn thành.';
  } finally {
    if (cdp) {
      try { await Promise.race([cdp.send('Browser.close'), pause(2000)]); } catch { }
      cdp.close();
    }
    if (child.exitCode === null) child.kill();
  }
}

main().then(message => { process.stdin.pause(); process.stdout.write(message + '\n'); }, error => {
  process.stdin.pause();
  process.stderr.write((error && error.message ? error.message : String(error)) + '\n');
  process.exitCode = 1;
});
