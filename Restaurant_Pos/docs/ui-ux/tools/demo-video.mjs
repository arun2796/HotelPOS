// Builds the narrated demo videos from the demo pack (docs/demo): one MP4 per role plus one full walkthrough.
//
// Each storyboard row in docs/demo/README.md (| # | picture | narration |) becomes one slide: the screenshot framed on
// a 1920x1080 title-safe canvas with the narration as a caption, spoken by a Windows voice (demo-video-tts.ps1,
// offline). Slide length = speech length + a short pause. Every role video opens with a card that names the role and
// its responsibilities; the full video adds an opening and a closing card.
//
// Output (OUT_DIR, default <repo>/artifacts/demo-video, gitignored):
//   01-admin.mp4 … 05-manager.mp4, HotelPOS-full-walkthrough.mp4, narration-script.md (what is said, per slide),
//   work/ (slide PNGs, WAVs, per-slide clips; delete to rebuild everything).
//
// Run from a scratch folder that has Playwright installed (the same one as demo-web-flow.mjs):
//   node <repo>/docs/ui-ux/tools/demo-video.mjs
// Env: OUT_DIR, DEMO_DIR (default <repo>/docs/demo), VOICE (default 'Microsoft Zira Desktop'), RATE (-10..10),
//      FFMPEG (ffmpeg.exe path; default: the winget Gyan.FFmpeg build or 'ffmpeg' on PATH), PW_CHANNEL (default msedge),
//      ROLES (comma list of chapter numbers to build, default all), PAUSE (seconds after each line, default 0.8).

import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import { spawnSync } from 'node:child_process';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..', '..', '..');
const DEMO = process.env.DEMO_DIR ?? path.join(repo, 'docs', 'demo');
const OUT = process.env.OUT_DIR ?? path.join(repo, 'artifacts', 'demo-video');
const WORK = path.join(OUT, 'work');
const VOICE = process.env.VOICE ?? 'Microsoft Zira Desktop';
const RATE = process.env.RATE ?? '0';
const PAUSE = Number(process.env.PAUSE ?? '0.8');
const CHANNEL = process.env.PW_CHANNEL ?? 'msedge';
const ONLY = process.env.ROLES ? process.env.ROLES.split(',').map(s => s.trim()) : null;
const FFMPEG = process.env.FFMPEG ?? findFfmpeg();
const FFPROBE = FFMPEG.replace(/ffmpeg(\.exe)?$/i, (m, ext) => `ffprobe${ext ?? ''}`);

const require = createRequire(path.join(process.cwd(), 'package.json'));
const { chromium } = require('playwright');

// ---------------------------------------------------------------- storyboard

const ROLES = {
  '1': { file: '01-admin', role: 'Administrator', client: 'Windows desktop', title: 'Set up the restaurant',
    duties: ['Create staff users and assign roles', 'Define sections, tables and the floor plan', 'Maintain the menu, prices, taxes and pictures',
      'Configure payment methods, printers and settings', 'Review the audit log of every change'] },
  '2': { file: '02-waiter', role: 'Waiter', client: 'Phone (browser app)', title: 'Take orders on the floor',
    duties: ['See table status as a list or on the floor map', 'Take dine-in and takeaway orders with options and notes', 'Send orders to the kitchen and print the K.O.T.',
      'Hold drafts, mark items served, request the bill', 'Keep working when the Wi-Fi drops'] },
  '3': { file: '03-kitchen', role: 'Kitchen', client: 'Tablet (browser app)', title: 'Cook and serve',
    duties: ['Watch orders arrive live on the kitchen board', 'Open a ticket with item pictures and notes', 'Accept, start and mark tickets ready',
      'Notify waiters the moment food is ready'] },
  '4': { file: '04-cashier', role: 'Cashier', client: 'Windows desktop', title: 'Bill and collect',
    duties: ['Open and close the shift with a cash count', 'Bill tables and takeaway orders from the queue', 'Apply discounts; take cash, split and wallet payments',
      'Print invoices and watch the print queue', 'Record pay-outs and expenses; run the X report'] },
  '5': { file: '05-manager', role: 'Manager', client: 'Windows desktop', title: 'Run the business',
    duties: ['Live dashboard: sales, orders, occupancy, expenses, who is on duty', 'Manage reservations', 'Reports to Excel and PDF: daily, hourly, profit and loss, payments',
      'Day End pre-check and GST for the month', 'Audit log of every action'] },
};

const OPENING = {
  id: 'opening', kind: 'card', heading: 'HotelPOS', sub: 'Restaurant point of sale for one venue on one LAN',
  bullets: ['Five roles: administrator, waiter, kitchen, cashier, manager', 'Three clients: Windows terminals, phones and tablets in the browser, Android app',
    'Everything updates live across every device'],
  speech: 'Welcome to HotelPOS. One restaurant, five roles, three kinds of client on one local network. The administrator and the cashier work on Windows terminals, waiters on their phones, the kitchen on a tablet, and the manager on any of them. Everything updates live. Let us follow one day of service, role by role.',
};
const CLOSING = {
  id: 'closing', kind: 'card', heading: 'Built for the floor', sub: 'What runs underneath',
  bullets: ['Clients tolerate Wi-Fi drops and reconnect on their own', 'Terminals are licensed; seats are managed from Admin', 'Nightly backups and LAN self-update on the server',
    'GST-ready invoices and returns'],
  speech: 'Behind the screens: the clients tolerate network drops and reconnect on their own, every terminal is licensed and managed from the Admin screen, the server takes nightly backups and updates the terminals over the LAN, and invoices and returns are GST ready. Thank you for watching.',
};

function parseStoryboard() {
  const md = fs.readFileSync(path.join(DEMO, 'README.md'), 'utf8').split(/\r?\n/);
  const chapters = [];
  let current = null;
  for (const line of md) {
    const h = line.match(/^## Chapter (\d) — (.+?) \(`([^`/]+)\/`/);
    if (h) { current = { num: h[1], title: h[2], folder: h[3], slides: [] }; chapters.push(current); continue; }
    if (line.startsWith('## ')) current = null;
    const row = current && line.match(/^\| (\d+) \| `([^`]+)` \| (.+?) \|\s*$/);
    if (row) current.slides.push({ n: Number(row[1]), file: row[2], text: row[3] });
  }
  return chapters;
}

const captionOf = t => t.replace(/\*\*\(re-shot\)\*\*/g, '').replace(/\*\*/g, '').replace(/`/g, '').replace(/\s+/g, ' ').trim();
const speechOf = t => captionOf(t)
  .replace(/\bKOT\b/g, 'K.O.T.').replace(/\bPWA\b/g, 'P.W.A.').replace(/\bKDS\b/g, 'K.D.S.').replace(/\bGST\b/g, 'G.S.T.')
  .replace(/\bX report\b/g, 'X report').replace(/₹/g, 'rupees ').replace(/×/g, ' by ').replace(/→/g, ' then ')
  .replace(/\bTA-…/g, 'T A').replace(/…/g, '.').replace(/\bINV-\S+/g, m => m.replace(/-/g, ' '))
  .replace(/\bT0?(\d+)\b/g, 'table $1').replace(/\bPhase \d+\b/g, 'the latest release').replace(/\s+/g, ' ').trim();

// ---------------------------------------------------------------- rendering

const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const CSS = `
  * { box-sizing: border-box; margin: 0; padding: 0; }
  html, body { width: 1920px; height: 1080px; overflow: hidden; background: #0b1220; color: #e5e7eb;
    font-family: 'Segoe UI', Inter, system-ui, sans-serif; }
  .top { position: absolute; left: 0; right: 0; top: 0; height: 84px; padding: 0 56px; display: flex; align-items: center;
    justify-content: space-between; border-bottom: 1px solid #1f2937; }
  .brand { font-size: 30px; font-weight: 700; letter-spacing: .5px; color: #f9fafb; }
  .brand b { color: #22c55e; }
  .chip { font-size: 22px; color: #cbd5e1; display: flex; gap: 18px; align-items: center; }
  .chip span { background: #14532d; color: #bbf7d0; padding: 6px 16px; border-radius: 999px; font-weight: 600; }
  .stage { position: absolute; left: 0; right: 0; top: 84px; bottom: 176px; display: flex; align-items: center; justify-content: center; }
  .stage img { max-width: 1760px; max-height: 780px; border-radius: 12px; box-shadow: 0 24px 64px rgba(0,0,0,.55); border: 1px solid #334155; }
  .cap { position: absolute; left: 0; right: 0; bottom: 0; height: 176px; padding: 26px 56px; background: #111827; border-top: 1px solid #1f2937;
    display: flex; gap: 28px; align-items: center; }
  .step { flex: none; width: 84px; height: 84px; border-radius: 50%; background: #22c55e; color: #052e16; font-size: 36px; font-weight: 800;
    display: flex; align-items: center; justify-content: center; }
  .text { font-size: 34px; line-height: 1.3; color: #f1f5f9; }
  .card { position: absolute; inset: 0; display: flex; flex-direction: column; justify-content: center; padding: 0 200px; }
  .card h1 { font-size: 92px; font-weight: 800; color: #f9fafb; letter-spacing: -1px; }
  .card h1 b { color: #22c55e; }
  .card h2 { font-size: 40px; font-weight: 500; color: #94a3b8; margin: 18px 0 56px; }
  .card li { list-style: none; font-size: 38px; line-height: 1.35; margin: 14px 0; padding-left: 52px; position: relative; color: #e2e8f0; }
  .card li::before { content: ''; position: absolute; left: 0; top: 16px; width: 22px; height: 22px; border-radius: 6px; background: #22c55e; }
  .foot { position: absolute; left: 56px; right: 56px; bottom: 40px; font-size: 24px; color: #64748b; display: flex; justify-content: space-between; }
`;

function slideHtml(ch, s, img) {
  return `<!doctype html><html><head><meta charset="utf-8"><style>${CSS}</style></head><body>
    <div class="top"><div class="brand">Hotel<b>POS</b></div>
      <div class="chip">${esc(ch.title)} <span>${esc(ROLES[ch.num].role)}</span> ${esc(ROLES[ch.num].client)}</div></div>
    <div class="stage"><img src="data:image/png;base64,${img}"></div>
    <div class="cap"><div class="step">${s.n}</div><div class="text">${esc(captionOf(s.text))}</div></div>
  </body></html>`;
}

function cardHtml(c) {
  return `<!doctype html><html><head><meta charset="utf-8"><style>${CSS}</style></head><body>
    <div class="top"><div class="brand">Hotel<b>POS</b></div><div class="chip">${esc(c.chipText ?? 'Product walkthrough')}</div></div>
    <div class="card"><h1>${c.headingHtml ?? esc(c.heading)}</h1><h2>${esc(c.sub)}</h2><ul>${c.bullets.map(b => `<li>${esc(b)}</li>`).join('')}</ul></div>
    <div class="foot"><div>${esc(c.foot ?? '')}</div><div>Captured from the real application, nothing mocked</div></div>
  </body></html>`;
}

// ---------------------------------------------------------------- helpers

function findFfmpeg() {
  const winget = path.join(os.homedir(), 'AppData', 'Local', 'Microsoft', 'WinGet', 'Packages');
  if (fs.existsSync(winget)) {
    for (const pkg of fs.readdirSync(winget).filter(d => /ffmpeg/i.test(d))) {
      for (const build of fs.readdirSync(path.join(winget, pkg))) {
        const exe = path.join(winget, pkg, build, 'bin', 'ffmpeg.exe');
        if (fs.existsSync(exe)) return exe;
      }
    }
  }
  return 'ffmpeg';
}

function run(cmd, args, label) {
  const r = spawnSync(cmd, args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (r.status !== 0) throw new Error(`${label ?? cmd} failed (${r.status}):\n${(r.stderr || r.stdout || '').slice(-3000)}`);
  return r.stdout;
}

const duration = file => Number(run(FFPROBE, ['-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', file]).trim());

function clip(png, wav, mp4) {
  if (fs.existsSync(mp4)) return;
  const len = (duration(wav) + PAUSE).toFixed(3);
  run(FFMPEG, ['-y', '-v', 'error', '-loop', '1', '-framerate', '30', '-i', png, '-i', wav,
    '-t', len, '-af', `apad=pad_dur=${PAUSE + 1}`, '-c:v', 'libx264', '-tune', 'stillimage', '-preset', 'veryfast', '-crf', '21',
    '-pix_fmt', 'yuv420p', '-r', '30', '-c:a', 'aac', '-b:a', '96k', '-ar', '48000', '-ac', '1', '-shortest', '-movflags', '+faststart', mp4], 'ffmpeg clip');
}

function concat(clips, mp4) {
  const list = mp4 + '.txt';
  fs.writeFileSync(list, clips.map(c => `file '${c.replace(/\\/g, '/').replace(/'/g, "'\\''")}'`).join('\n'));
  run(FFMPEG, ['-y', '-v', 'error', '-f', 'concat', '-safe', '0', '-i', list, '-c', 'copy', '-movflags', '+faststart', mp4], 'ffmpeg concat');
  fs.unlinkSync(list);
}

const fmt = s => `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, '0')}`;

// ---------------------------------------------------------------- main

const t0 = Date.now();
fs.mkdirSync(WORK, { recursive: true });
const chapters = parseStoryboard().filter(c => !ONLY || ONLY.includes(c.num));
if (!chapters.length) throw new Error('no chapters parsed from ' + path.join(DEMO, 'README.md'));

// 1. Everything that will be spoken, with ids = file stems.
const lines = [];
const items = []; // { id, html, speech, chapter|null }
items.push({ id: OPENING.id, html: cardHtml(OPENING), speech: OPENING.speech });
for (const ch of chapters) {
  const r = ROLES[ch.num];
  const card = { heading: r.role, headingHtml: `${esc(r.role)}`, sub: `${ch.title} · ${r.client}`, bullets: r.duties, chipText: `Chapter ${ch.num}`, foot: `${ch.slides.length} screens` };
  const dutiesSpoken = r.duties.map(d => d.replace(/;/g, ',')).join('. ');
  items.push({ id: `${r.file}-00-card`, html: cardHtml(card), chapter: ch.num,
    speech: `Chapter ${ch.num}. ${ch.title}. The ${r.role.toLowerCase()} works on a ${r.client.replace(/\s*\(browser app\)/, ' in the browser')}. Responsibilities: ${dutiesSpoken}.` });
  for (const s of ch.slides) {
    const png = path.join(DEMO, ch.folder, s.file);
    if (!fs.existsSync(png)) throw new Error('missing screenshot ' + png);
    items.push({ id: `${r.file}-${String(s.n).padStart(2, '0')}`, html: () => slideHtml(ch, s, fs.readFileSync(png).toString('base64')), speech: speechOf(s.text), chapter: ch.num, step: s });
  }
}
items.push({ id: CLOSING.id, html: cardHtml(CLOSING), speech: CLOSING.speech });
for (const it of items) lines.push({ id: it.id, text: it.speech });

// 2. Speech (skips WAVs that already exist).
const linesFile = path.join(WORK, 'lines.json');
fs.writeFileSync(linesFile, JSON.stringify(lines, null, 1), 'utf8');
console.log(run('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(here, 'demo-video-tts.ps1'),
  '-Lines', linesFile, '-OutDir', WORK, '-Voice', VOICE, '-Rate', RATE], 'tts').trim());

// 3. Slide frames.
const browser = await chromium.launch({ channel: CHANNEL });
const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });
let rendered = 0;
for (const it of items) {
  const png = path.join(WORK, `${it.id}.png`);
  if (fs.existsSync(png)) continue;
  await page.setContent(typeof it.html === 'function' ? it.html() : it.html, { waitUntil: 'load' });
  await page.screenshot({ path: png });
  rendered++;
}
await browser.close();
console.log(`rendered ${rendered} new frames of ${items.length}`);

// 4. Clips, then one MP4 per role and the full walkthrough.
let encoded = 0;
for (const it of items) {
  const mp4 = path.join(WORK, `${it.id}.mp4`);
  if (!fs.existsSync(mp4)) encoded++;
  clip(path.join(WORK, `${it.id}.png`), path.join(WORK, `${it.id}.wav`), mp4);
}
console.log(`encoded ${encoded} new clips`);

const script = ['# HotelPOS demo videos — narration script', '', `Voice: ${VOICE} (rate ${RATE}); pause after each line ${PAUSE}s.`, ''];
const summary = [];
const fullList = [path.join(WORK, `${OPENING.id}.mp4`)];
for (const ch of chapters) {
  const r = ROLES[ch.num];
  const clips = items.filter(i => i.chapter === ch.num).map(i => path.join(WORK, `${i.id}.mp4`));
  const out = path.join(OUT, `${r.file}.mp4`);
  concat(clips, out);
  fullList.push(...clips);
  const len = duration(out);
  summary.push({ file: path.basename(out), role: r.role, slides: ch.slides.length, len });
  script.push(`## ${path.basename(out)} — ${r.role} (${fmt(len)})`, '');
  for (const i of items.filter(i => i.chapter === ch.num)) script.push(`- **${i.id}** ${i.speech}`);
  script.push('');
}
fullList.push(path.join(WORK, `${CLOSING.id}.mp4`));
const full = path.join(OUT, 'HotelPOS-full-walkthrough.mp4');
concat(fullList, full);
const fullLen = duration(full);
script.unshift(`Full walkthrough: ${fmt(fullLen)}. Opening: ${OPENING.speech}`, '', `Closing: ${CLOSING.speech}`, '');
fs.writeFileSync(path.join(OUT, 'narration-script.md'), script.join('\n'));

for (const s of summary) console.log(`${s.file.padEnd(16)} ${s.role.padEnd(14)} ${String(s.slides).padStart(3)} screens  ${fmt(s.len)}`);
console.log(`${path.basename(full).padEnd(31)} ${fmt(fullLen)}  (${(fs.statSync(full).size / 1048576).toFixed(0)} MB)`);
console.log(`done in ${((Date.now() - t0) / 1000).toFixed(0)}s -> ${OUT}`);
