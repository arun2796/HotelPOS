// Shared pieces of the demo video builders (demo-video.mjs = slideshow from the screenshots, demo-video-live.mjs = cut
// from the recorded live runs): the role cards and their speech, the 1920x1080 frame design, ffmpeg helpers and the
// Windows text-to-speech bridge (demo-video-tts.ps1).

import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import { spawnSync } from 'node:child_process';

export const here = path.dirname(fileURLToPath(import.meta.url));
export const repo = path.resolve(here, '..', '..', '..');

export const ROLES = {
  '1': { file: '01-admin', role: 'Administrator', client: 'Windows desktop', title: 'Set up the restaurant', folders: ['01-admin-setup'],
    duties: ['Create staff users and assign roles', 'Define sections, tables and the floor plan', 'Maintain the menu, prices, taxes and pictures',
      'Configure payment methods, printers and settings', 'Review the audit log of every change'] },
  '2': { file: '02-waiter', role: 'Waiter', client: 'Phone (browser app)', title: 'Take orders on the floor', folders: ['02-waiter-phone'],
    duties: ['See table status as a list or on the floor map', 'Take dine-in and takeaway orders with options and notes', 'Send orders to the kitchen and print the K.O.T.',
      'Hold drafts, mark items served, request the bill', 'Keep working when the Wi-Fi drops'] },
  '3': { file: '03-kitchen', role: 'Kitchen', client: 'Tablet (browser app)', title: 'Cook and serve', folders: ['03-kitchen-tablet'],
    duties: ['Watch orders arrive live on the kitchen board', 'Open a ticket with item pictures and notes', 'Accept, start and mark tickets ready',
      'Notify waiters the moment food is ready'] },
  '4': { file: '04-cashier', role: 'Cashier', client: 'Windows desktop', title: 'Bill and collect', folders: ['04-cashier-desktop'],
    duties: ['Open and close the shift with a cash count', 'Bill tables and takeaway orders from the queue', 'Apply discounts; take cash, split and wallet payments',
      'Print invoices and watch the print queue', 'Record pay-outs and expenses; run the X report'] },
  '5': { file: '05-manager', role: 'Manager', client: 'Windows desktop', title: 'Run the business', folders: ['05-manager-desktop'],
    duties: ['Live dashboard: sales, orders, occupancy, expenses, who is on duty', 'Manage reservations', 'Reports to Excel and PDF: daily, hourly, profit and loss, payments',
      'Day End pre-check and GST for the month', 'Audit log of every action'] },
};

export const OPENING = {
  id: 'opening', heading: 'HotelPOS', sub: 'Restaurant point of sale for one venue on one LAN', chipText: 'Product walkthrough',
  bullets: ['Five roles: administrator, waiter, kitchen, cashier, manager', 'Three clients: Windows terminals, phones and tablets in the browser, Android app',
    'Everything updates live across every device'],
  speech: 'Welcome to HotelPOS. One restaurant, five roles, and three kinds of client on one local network. The administrator and the cashier work on Windows terminals, the waiters on their phones, the kitchen on a tablet, and the manager on any of them. Everything updates live. Let us follow one day of service, role by role.',
};
export const CLOSING = {
  id: 'closing', heading: 'Built for the floor', sub: 'What runs underneath', chipText: 'Product walkthrough',
  bullets: ['Clients tolerate Wi-Fi drops and reconnect on their own', 'Terminals are licensed; seats are managed from Admin', 'Nightly backups and LAN self-update on the server',
    'GST-ready invoices and returns'],
  speech: 'Behind the screens: the clients tolerate network drops and reconnect on their own, every terminal is licensed and managed from the Admin screen, the server takes nightly backups and updates the terminals over the network, and the invoices and returns are GST ready. Thank you for watching.',
};

export function roleCard(num, screens) {
  const r = ROLES[num];
  const client = r.client.replace(/\s*\(browser app\)/, ' in the browser');
  return {
    id: `${r.file}-00-card`, heading: r.role, sub: `${r.title} · ${r.client}`, bullets: r.duties, chipText: `Chapter ${num}`, foot: screens ? `${screens} screens` : '',
    speech: `Chapter ${num}. ${r.title}. The ${r.role.toLowerCase()} works on a ${client}. Responsibilities: ${r.duties.map(d => d.replace(/;/g, ',')).join('. ')}.`,
  };
}

// ---------------------------------------------------------------- frame design (1920x1080)

export const STAGE = { top: 84, height: 820, maxWidth: 1760, maxHeight: 800 };

export const CSS = `
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
  .card h2 { font-size: 40px; font-weight: 500; color: #94a3b8; margin: 18px 0 56px; }
  .card li { list-style: none; font-size: 38px; line-height: 1.35; margin: 14px 0; padding-left: 52px; position: relative; color: #e2e8f0; }
  .card li::before { content: ''; position: absolute; left: 0; top: 16px; width: 22px; height: 22px; border-radius: 6px; background: #22c55e; }
  .foot { position: absolute; left: 56px; right: 56px; bottom: 40px; font-size: 24px; color: #64748b; display: flex; justify-content: space-between; }
`;

export const esc = s => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

// A step frame: header with chapter and role, the stage (a screenshot, or empty when a video is overlaid later), caption.
export function frameHtml(num, stepNo, caption, imageBase64 = null) {
  const r = ROLES[num];
  return `<!doctype html><html><head><meta charset="utf-8"><style>${CSS}</style></head><body>
    <div class="top"><div class="brand">Hotel<b>POS</b></div>
      <div class="chip">${esc(r.title)} <span>${esc(r.role)}</span> ${esc(r.client)}</div></div>
    <div class="stage">${imageBase64 ? `<img src="data:image/png;base64,${imageBase64}">` : ''}</div>
    <div class="cap"><div class="step">${stepNo}</div><div class="text">${esc(caption)}</div></div>
  </body></html>`;
}

export function cardHtml(c) {
  return `<!doctype html><html><head><meta charset="utf-8"><style>${CSS}</style></head><body>
    <div class="top"><div class="brand">Hotel<b>POS</b></div><div class="chip">${esc(c.chipText ?? 'Product walkthrough')}</div></div>
    <div class="card"><h1>${esc(c.heading)}</h1><h2>${esc(c.sub)}</h2><ul>${c.bullets.map(b => `<li>${esc(b)}</li>`).join('')}</ul></div>
    <div class="foot"><div>${esc(c.foot ?? '')}</div><div>Captured from the real application, nothing mocked</div></div>
  </body></html>`;
}

// Renders HTML pages to PNG files with the Playwright that is installed in the current working directory.
export async function renderFrames(items, channel = process.env.PW_CHANNEL ?? 'msedge') {
  const todo = items.filter(i => !fs.existsSync(i.png));
  if (!todo.length) return 0;
  const require = createRequire(path.join(process.cwd(), 'package.json'));
  const { chromium } = require('playwright');
  const browser = await chromium.launch({ channel });
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });
  for (const it of todo) {
    await page.setContent(typeof it.html === 'function' ? it.html() : it.html, { waitUntil: 'load' });
    await page.screenshot({ path: it.png });
  }
  await browser.close();
  return todo.length;
}

// ---------------------------------------------------------------- ffmpeg

export function findFfmpeg() {
  if (process.env.FFMPEG) return process.env.FFMPEG;
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

export const FFMPEG = findFfmpeg();
export const FFPROBE = FFMPEG.replace(/ffmpeg(\.exe)?$/i, (m, ext) => `ffprobe${ext ?? ''}`);

export function run(cmd, args, label) {
  const r = spawnSync(cmd, args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (r.status !== 0) throw new Error(`${label ?? cmd} failed (${r.status}):\n${(r.stderr || r.stdout || '').slice(-3000)}`);
  return r.stdout;
}

export const duration = file => Number(run(FFPROBE, ['-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', file]).trim());

export const ENCODE = ['-c:v', 'libx264', '-preset', 'veryfast', '-crf', '21', '-pix_fmt', 'yuv420p', '-r', '30',
  '-c:a', 'aac', '-b:a', '96k', '-ar', '48000', '-ac', '1', '-movflags', '+faststart'];

// A still frame held for the length of its speech plus a pause.
export function clipStill(png, wav, mp4, pause = 0.8) {
  if (fs.existsSync(mp4)) return false;
  const len = (duration(wav) + pause).toFixed(3);
  run(FFMPEG, ['-y', '-v', 'error', '-loop', '1', '-framerate', '30', '-i', png, '-i', wav, '-t', len, '-af', `apad=pad_dur=${pause + 1}`,
    '-tune', 'stillimage', ...ENCODE, '-shortest', mp4], 'ffmpeg still');
  return true;
}

// A cut of a recorded video, scaled into the stage of a frame PNG, with the step's speech as the sound track.
// crop: "w:h:x:y" when the recording holds the page in a corner of a larger frame (Playwright pads a recordVideo size
// larger than the viewport instead of scaling).
export function clipLive(src, start, len, framePng, wav, mp4, crop = null) {
  if (fs.existsSync(mp4)) return false;
  const { top, height, maxWidth, maxHeight } = STAGE;
  const filter = `[0:v]${crop ? `crop=${crop},` : ''}scale=${maxWidth}:${maxHeight}:force_original_aspect_ratio=decrease:flags=lanczos,setsar=1[v];` +
    `[1:v][v]overlay=(main_w-overlay_w)/2:${top}+(${height}-overlay_h)/2:format=auto,format=yuv420p[out];[2:a]apad[a]`;
  run(FFMPEG, ['-y', '-v', 'error', '-ss', start.toFixed(3), '-t', len.toFixed(3), '-i', src, '-loop', '1', '-framerate', '30', '-i', framePng, '-i', wav,
    '-filter_complex', filter, '-map', '[out]', '-map', '[a]', '-t', len.toFixed(3), ...ENCODE, mp4], 'ffmpeg live clip');
  return true;
}

export function concat(clips, mp4) {
  const list = mp4 + '.txt';
  fs.writeFileSync(list, clips.map(c => `file '${c.replace(/\\/g, '/').replace(/'/g, "'\\''")}'`).join('\n'));
  run(FFMPEG, ['-y', '-v', 'error', '-f', 'concat', '-safe', '0', '-i', list, '-c', 'copy', '-movflags', '+faststart', mp4], 'ffmpeg concat');
  fs.unlinkSync(list);
}

export const fmt = s => `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, '0')}`;

// ---------------------------------------------------------------- speech

// Speaks [{ id, text }] to <dir>/<id>.wav (existing files are kept) and returns { id: seconds }.
export function speak(lines, dir, { voice = process.env.VOICE ?? 'Microsoft Zira Desktop', rate = process.env.RATE ?? '0' } = {}) {
  fs.mkdirSync(dir, { recursive: true });
  const file = path.join(dir, 'lines.json');
  fs.writeFileSync(file, JSON.stringify(lines, null, 1), 'utf8');
  const out = run('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(here, 'demo-video-tts.ps1'),
    '-Lines', file, '-OutDir', dir, '-Voice', voice, '-Rate', String(rate)], 'tts');
  console.log(out.trim());
  const seconds = {};
  for (const l of lines) seconds[l.id] = duration(path.join(dir, `${l.id}.wav`));
  return seconds;
}

// Text spoken for a caption: abbreviations spelt out, symbols read.
export const speechOf = t => String(t)
  .replace(/\bKOT\b/g, 'K.O.T.').replace(/\bPWA\b/g, 'P.W.A.').replace(/\bKDS\b/g, 'K.D.S.').replace(/\bGST\b/g, 'G.S.T.').replace(/\bUPI\b/g, 'U.P.I.')
  .replace(/₹/g, 'rupees ').replace(/×/g, ' by ').replace(/→/g, ' then ').replace(/…/g, '.').replace(/\s+/g, ' ').trim();
