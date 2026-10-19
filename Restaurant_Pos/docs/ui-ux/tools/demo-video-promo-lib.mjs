// Promo design for the demo videos, after the owner's BillingHub reference clip: dark navy scene with two soft glows and a
// faint grid, a spaced-caps eyebrow, a bold headline whose last word carries the cyan-to-teal accent, the app inside a
// window (or phone) mockup with a soft shadow, pills or a subtitle at the bottom, and a quiet music bed under the voice.

import fs from 'node:fs';
import { esc, run, FFMPEG, ENCODE, duration } from './demo-video-lib.mjs';

export const PROMO_CSS = `
  * { box-sizing: border-box; margin: 0; padding: 0; }
  html, body { width: 1920px; height: 1080px; overflow: hidden; color: #e5e7eb; font-family: 'Segoe UI', Inter, system-ui, sans-serif;
    background: #07101f; }
  .bg { position: absolute; inset: 0;
    background:
      radial-gradient(900px 520px at 18% 22%, rgba(37, 99, 235, .42), transparent 65%),
      radial-gradient(760px 480px at 84% 86%, rgba(13, 148, 136, .34), transparent 65%),
      linear-gradient(180deg, #0a1628 0%, #0b1a33 55%, #07101f 100%); }
  .grid { position: absolute; inset: 0; opacity: .14;
    background-image: linear-gradient(rgba(148,163,184,.35) 1px, transparent 1px), linear-gradient(90deg, rgba(148,163,184,.35) 1px, transparent 1px);
    background-size: 72px 72px; -webkit-mask-image: radial-gradient(1200px 700px at 50% 55%, #000 30%, transparent 100%);
    mask-image: radial-gradient(1200px 700px at 50% 55%, #000 30%, transparent 100%); }
  .eyebrow { position: absolute; left: 0; right: 0; top: 54px; text-align: center; font-size: 20px; letter-spacing: .32em; text-transform: uppercase;
    color: #38bdf8; font-weight: 600; }
  .headline { position: absolute; left: 0; right: 0; top: 86px; text-align: center; font-size: 58px; font-weight: 800; letter-spacing: -.5px; color: #f8fafc; }
  .headline b, .card h1 b { background: linear-gradient(90deg, #38bdf8, #2dd4bf); -webkit-background-clip: text; background-clip: text; color: transparent; }
  .mock { position: absolute; border-radius: 14px; background: #0f172a; box-shadow: 0 40px 90px rgba(0,0,0,.6), 0 0 0 1px rgba(148,163,184,.18), 0 0 120px rgba(56,189,248,.12); }
  .mock .bar { position: absolute; left: 0; right: 0; top: 0; height: 36px; border-radius: 14px 14px 0 0; background: #111c33; display: flex; align-items: center; gap: 8px; padding: 0 16px;
    font-size: 14px; color: #94a3b8; }
  .mock .bar i { width: 11px; height: 11px; border-radius: 50%; display: inline-block; }
  .mock .bar span { margin-left: 10px; }
  .phone { border-radius: 44px; box-shadow: 0 40px 90px rgba(0,0,0,.6), 0 0 0 10px #0f172a, 0 0 0 12px rgba(148,163,184,.25), 0 0 120px rgba(56,189,248,.12); }
  .subtitle { position: absolute; left: 160px; right: 160px; bottom: 44px; text-align: center; font-size: 30px; line-height: 1.3; color: #cbd5e1; }
  .pills { position: absolute; left: 0; right: 0; bottom: 56px; display: flex; justify-content: center; gap: 16px; flex-wrap: wrap; padding: 0 160px; }
  .pill { padding: 12px 24px; border-radius: 999px; border: 1px solid rgba(148,163,184,.35); background: rgba(15,23,42,.7); font-size: 22px; font-weight: 600; color: #e2e8f0; }
  .pill.small { font-size: 17px; padding: 8px 18px; color: #cbd5e1; }
  .card { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; text-align: center; padding: 0 200px; }
  .card .eyebrow { position: static; margin-bottom: 26px; }
  .card h1 { font-size: 104px; font-weight: 800; letter-spacing: -2px; color: #f8fafc; line-height: 1.05; }
  .card h2 { font-size: 32px; font-weight: 400; color: #94a3b8; margin-top: 22px; max-width: 1300px; line-height: 1.35; }
  .card .pills { position: static; margin-top: 52px; }
  .brand { position: absolute; left: 56px; top: 44px; font-size: 26px; font-weight: 700; color: #f8fafc; }
  .brand b { color: #2dd4bf; }
`;

// Scene themes: navy is the reference look; emerald uses the HotelPOS green with a diagonal light sweep.
export const THEMES = {
  navy: '',
  emerald: `
    html, body { background: #04100c; }
    .bg { background:
        linear-gradient(115deg, transparent 38%, rgba(134,239,172,.10) 50%, transparent 62%),
        radial-gradient(900px 540px at 16% 20%, rgba(22,163,74,.46), transparent 65%),
        radial-gradient(800px 500px at 86% 84%, rgba(20,184,166,.36), transparent 65%),
        linear-gradient(180deg, #06140f 0%, #071a14 55%, #04100c 100%); }
    .grid { opacity: .12; background-image: linear-gradient(rgba(134,239,172,.35) 1px, transparent 1px), linear-gradient(90deg, rgba(134,239,172,.35) 1px, transparent 1px); }
    .eyebrow, .brand b { color: #4ade80; }
    .headline b, .card h1 b { background: linear-gradient(90deg, #4ade80, #2dd4bf); -webkit-background-clip: text; background-clip: text; color: transparent; }
    .mock { background: #0a1f17; box-shadow: 0 40px 90px rgba(0,0,0,.62), 0 0 0 1px rgba(134,239,172,.2), 0 0 140px rgba(74,222,128,.16); }
    .mock .bar { background: #0d2a1f; }
    .phone { box-shadow: 0 40px 90px rgba(0,0,0,.62), 0 0 0 10px #0a1f17, 0 0 0 12px rgba(134,239,172,.3), 0 0 140px rgba(74,222,128,.16); }
    .pill { border-color: rgba(134,239,172,.32); background: rgba(6,20,15,.72); }
  `,
};
const css = theme => PROMO_CSS + (THEMES[theme] ?? '');

// Headline with the last word accented ("Floor plan: save" -> "Floor plan: <b>save.</b>").
export function accentLast(text) {
  const t = String(text).trim().replace(/\.$/, '') + '.';
  const i = t.lastIndexOf(' ');
  return i < 0 ? `<b>${esc(t)}</b>` : `${esc(t.slice(0, i + 1))}<b>${esc(t.slice(i + 1))}</b>`;
}

// Fits a source into the mockup area; returns the screen rectangle the video is overlaid on.
export function fitBox(srcW, srcH, kind) {
  const maxW = kind === 'phone' ? 420 : 1440;
  const maxH = kind === 'phone' ? 760 : 724;
  const s = Math.min(maxW / srcW, maxH / srcH);
  const w = Math.round(srcW * s / 2) * 2;
  const h = Math.round(srcH * s / 2) * 2;
  return { x: Math.round((1920 - w) / 2), y: kind === 'phone' ? 196 : 236, w, h, kind };
}

export function promoFrameHtml({ eyebrow, headline, box, subtitle, title, theme = 'navy' }) {
  const mock = box.kind === 'phone'
    ? `<div class="mock phone" style="left:${box.x}px;top:${box.y}px;width:${box.w}px;height:${box.h}px"></div>`
    : `<div class="mock" style="left:${box.x}px;top:${box.y - 36}px;width:${box.w}px;height:${box.h + 36}px">
         <div class="bar"><i style="background:#f87171"></i><i style="background:#fbbf24"></i><i style="background:#34d399"></i><span>${esc(title ?? 'HotelPOS')}</span></div></div>`;
  return `<!doctype html><html><head><meta charset="utf-8"><style>${css(theme)}</style></head><body>
    <div class="bg"></div><div class="grid"></div><div class="brand">Hotel<b>POS</b></div>
    <div class="eyebrow">${esc(eyebrow)}</div><div class="headline">${accentLast(headline)}</div>
    ${mock}
    <div class="subtitle">${esc(subtitle)}</div>
  </body></html>`;
}

export function promoCardHtml({ eyebrow, headline, sub, pills = [], small = false, theme = 'navy' }) {
  return `<!doctype html><html><head><meta charset="utf-8"><style>${css(theme)}</style></head><body>
    <div class="bg"></div><div class="grid"></div><div class="brand">Hotel<b>POS</b></div>
    <div class="card"><div class="eyebrow">${esc(eyebrow)}</div><h1>${accentLast(headline)}</h1>${sub ? `<h2>${esc(sub)}</h2>` : ''}
      <div class="pills">${pills.map(p => `<span class="pill${small ? ' small' : ''}">${esc(p)}</span>`).join('')}</div></div>
  </body></html>`;
}

// A cut of a recording placed exactly on the mockup screen of a promo frame, fading in, with the step's speech.
export function clipPromo(src, start, len, framePng, wav, mp4, box, crop = null) {
  if (fs.existsSync(mp4)) return false;
  const filter = `[0:v]${crop ? `crop=${crop},` : ''}scale=${box.w}:${box.h}:flags=lanczos,setsar=1[v];` +
    `[1:v][v]overlay=${box.x}:${box.y}:format=auto,format=yuv420p,fade=t=in:st=0:d=0.35[out];[2:a]apad[a]`;
  run(FFMPEG, ['-y', '-v', 'error', '-ss', start.toFixed(3), '-t', len.toFixed(3), '-i', src, '-loop', '1', '-framerate', '30', '-i', framePng, '-i', wav,
    '-filter_complex', filter, '-map', '[out]', '-map', '[a]', '-t', len.toFixed(3), ...ENCODE, mp4], 'ffmpeg promo clip');
  return true;
}

export function clipPromoStill(png, wav, mp4, pause = 1.0) {
  if (fs.existsSync(mp4)) return false;
  const len = (duration(wav) + pause).toFixed(3);
  run(FFMPEG, ['-y', '-v', 'error', '-loop', '1', '-framerate', '30', '-i', png, '-i', wav, '-t', len, '-af', `apad=pad_dur=${pause + 1}`,
    '-vf', 'fade=t=in:st=0:d=0.5', '-tune', 'stillimage', ...ENCODE, '-shortest', mp4], 'ffmpeg promo still');
  return true;
}

// A quiet synthesized electronic bed (pad chords, soft kick, hat, bass): 44.1 kHz mono 16-bit WAV, so the videos need
// no licensed track. Replace with a real one via MUSIC=<file>.
export function writeMusic(file, seconds, bpm = 96) {
  const sr = 44100;
  const n = Math.ceil(seconds * sr);
  const out = new Int16Array(n);
  const beat = 60 / bpm;
  const chords = [[220.0, 261.63, 329.63], [174.61, 220.0, 261.63], [130.81, 164.81, 196.0], [196.0, 246.94, 293.66]]; // Am F C G
  const bass = [110.0, 87.31, 65.41, 98.0];
  const barLen = beat * 4;
  for (let i = 0; i < n; i++) {
    const t = i / sr;
    const bar = Math.floor(t / barLen) % 4;
    const chord = chords[bar];
    const inBar = t % barLen;
    const inBeat = t % beat;
    let v = 0;
    const env = Math.min(1, inBar / 1.2) * (1 - Math.max(0, (inBar - barLen + 0.6) / 0.6));
    for (const f of chord) {
      v += (Math.sin(2 * Math.PI * f * t) + 0.5 * Math.sin(2 * Math.PI * (f * 1.004) * t) + 0.5 * Math.sin(2 * Math.PI * (f * 0.996) * t)) * 0.045 * env;
    }
    const beatNo = Math.floor(inBar / beat);
    if (beatNo % 2 === 0) v += Math.sin(2 * Math.PI * bass[bar] * t) * 0.12 * Math.exp(-inBeat * 2.2);
    v += Math.sin(2 * Math.PI * (48 + 60 * Math.exp(-inBeat * 30)) * inBeat) * 0.22 * Math.exp(-inBeat * 9);
    const off = (inBeat + beat / 2) % beat;
    v += (Math.random() * 2 - 1) * 0.035 * Math.exp(-off * 40);
    const fade = Math.min(1, t / 2, Math.max(0, (seconds - t) / 3));
    out[i] = Math.max(-32767, Math.min(32767, Math.round(v * fade * 32767)));
  }
  const header = Buffer.alloc(44);
  header.write('RIFF', 0); header.writeUInt32LE(36 + n * 2, 4); header.write('WAVE', 8); header.write('fmt ', 12);
  header.writeUInt32LE(16, 16); header.writeUInt16LE(1, 20); header.writeUInt16LE(1, 22); header.writeUInt32LE(sr, 24);
  header.writeUInt32LE(sr * 2, 28); header.writeUInt16LE(2, 32); header.writeUInt16LE(16, 34); header.write('data', 36); header.writeUInt32LE(n * 2, 40);
  fs.writeFileSync(file, Buffer.concat([header, Buffer.from(out.buffer)]));
  return file;
}

// Lays the music under the voice (ducked while the voice speaks) and writes a new MP4; the video stream is copied.
export function mixMusic(video, music, out, level = 0.16) {
  const len = duration(video);
  const filter = `[1:a]atrim=0:${len.toFixed(3)},volume=${level},afade=t=out:st=${Math.max(0, len - 3).toFixed(3)}:d=3[m];` +
    `[m][0:a]sidechaincompress=threshold=0.03:ratio=8:attack=40:release=500:makeup=1[d];[0:a][d]amix=inputs=2:duration=first:normalize=0[a]`;
  run(FFMPEG, ['-y', '-v', 'error', '-i', video, '-i', music, '-filter_complex', filter, '-map', '0:v', '-map', '[a]', '-c:v', 'copy',
    '-c:a', 'aac', '-b:a', '128k', '-ar', '48000', '-ac', '2', '-movflags', '+faststart', out], 'ffmpeg music mix');
}
