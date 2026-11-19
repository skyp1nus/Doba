// Renders the Doba design prototype (dc component) to reference PNGs.
// Usage: node render.js [outDir] [only-name ...]
const { chromium } = require('playwright');
const http = require('http');
const fs = require('fs');
const path = require('path');

const ROOT = __dirname;
const SITE = path.join(ROOT, 'site');
const FONTS = path.join(ROOT, 'fonts');
const OUT = process.argv[2] || path.join(ROOT, 'out');
const ONLY = process.argv.slice(3);

const FONT_CSS = [
  ['Geist', 'Geist-Light.ttf', 300], ['Geist', 'Geist-Regular.ttf', 400], ['Geist', 'Geist-Medium.ttf', 500],
  ['Geist', 'Geist-SemiBold.ttf', 600], ['Geist', 'Geist-Bold.ttf', 700],
  ['Geist Mono', 'GeistMono-Regular.ttf', 400], ['Geist Mono', 'GeistMono-Medium.ttf', 500],
].map(([f, file, w]) => `@font-face{font-family:'${f}';src:url('/fonts/${file}') format('truetype');font-weight:${w};font-style:normal;font-display:block}`).join('\n');

function wrapper(attrs) {
  const a = Object.entries(attrs).map(([k, v]) => `${k}="${v}"`).join(' ');
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Render</title>
<script src="./support.js"></script>
<style>${FONT_CSS}</style>
</head>
<body>
<x-dc>
<helmet>
<style>
body{margin:0}
</style>
</helmet>
<div style="width: 1600px; height: 1000px">
<dc-import name="Main" ${a} hint-size="1600px,1000px"></dc-import>
</div>
</x-dc>
<script type="text/x-dc" data-dc-script data-props='{"$preview":{"width":1600,"height":1000}}'>
class Component extends DCLogic {
  renderVals() {
    return {};
  }
}
</script>
</body>
</html>`;
}

function serve() {
  return new Promise((res) => {
    const srv = http.createServer((req, resp) => {
      const u = new URL(req.url, 'http://x');
      let p = decodeURIComponent(u.pathname);
      if (p === '/w/') {
        const attrs = Object.fromEntries(u.searchParams.entries());
        resp.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
        return resp.end(wrapper(attrs));
      }
      // /w/ pages are served from "/w/" so relative ./support.js and ./Main.dc.html map below
      let file;
      if (p.startsWith('/fonts/')) file = path.join(FONTS, path.basename(p));
      else file = path.join(SITE, path.basename(p));
      fs.readFile(file, (err, data) => {
        if (err) { resp.writeHead(404); return resp.end('nf'); }
        const ext = path.extname(file);
        const ct = { '.js': 'text/javascript', '.html': 'text/html; charset=utf-8', '.ttf': 'font/ttf' }[ext] || 'application/octet-stream';
        resp.writeHead(200, { 'content-type': ct });
        resp.end(data);
      });
    });
    srv.listen(0, '127.0.0.1', () => res(srv));
  });
}

module.exports = { serve, wrapper };

async function settle(page) {
  // park the pointer on an empty part of the title bar so no :hover styles leak into the shot
  await page.mouse.move(300, 20);
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(900);
}

async function open(page, base, attrs) {
  const q = new URLSearchParams(attrs).toString();
  await page.goto(`${base}/w/?${q}`, { waitUntil: 'load' });
  await page.waitForFunction(() => {
    const t = document.body.innerText || '';
    return t.length > 50 && !t.includes('{{');
  }, null, { timeout: 20000 });
  await page.evaluate(async () => {
    await document.fonts.load("400 13px Geist"); await document.fonts.load("500 13px Geist");
    await document.fonts.load("600 13px Geist"); await document.fonts.load("400 13px 'Geist Mono'");
    await document.fonts.ready;
  });
  await page.waitForTimeout(900); // launch animation is 520 ms
}

if (require.main === module) (async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const srv = await serve();
  const base = `http://127.0.0.1:${srv.address().port}`;
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1600, height: 1000 }, deviceScaleFactor: 1 });
  await ctx.route(/fonts\.(googleapis|gstatic)\.com/, (r) => r.abort());
  const page = await ctx.newPage();
  page.on('console', (m) => { if (m.type() === 'error' || m.type() === 'warning') console.log('[console]', m.type(), m.text()); });
  page.on('pageerror', (e) => console.log('[pageerror]', e.message));

  const jobs = require('./jobs.js');
  for (const job of jobs) {
    if (ONLY.length && !ONLY.includes(job.name)) continue;
    await open(page, base, job.attrs);
    if (job.act) await job.act(page);
    await settle(page);
    const f = path.join(OUT, job.name + '.png');
    // job.clip crops the 1600×1000 frame ({ x, y, width, height } in CSS px), as the canvas's state artboards do
    await page.screenshot(job.clip ? { path: f, clip: job.clip } : { path: f });
    console.log('wrote', f);
  }
  await browser.close();
  srv.close();
})().catch((e) => { console.error(e); process.exit(1); });
