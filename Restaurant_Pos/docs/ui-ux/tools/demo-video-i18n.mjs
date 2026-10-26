// Full-flow walkthrough video: set-up chapter (server exe with its bundled PostgreSQL, terminal exe, first sign-in,
// recorded in Windows Sandbox by tools/sandbox) + the five role chapters cut from the recorded live runs + closing.
// Narration and all on-screen text come from i18n/narration.<lang>.json (English is the language kept; another file
// would add a language); the role-step lines of the English version fall back to demo-narration.json. Speech: Microsoft
// neural voices through edge-tts (needs internet), see demo-video-edge-tts.py. Frames: Playwright on the installed Edge.
//
// Run from a scratch folder that has Playwright installed (see web-flow.mjs):
//   set LANG_ID=en        node <repo>/docs/ui-ux/tools/demo-video-i18n.mjs
// Env: LANG_ID, RECORD_DIR (default <repo>/artifacts/demo-video-screen/record), OUT_DIR (default
//      <repo>/artifacts/demo-video-i18n/<lang>), EDGE_TTS_PY (python with edge-tts; default: the scratch venv), FFMPEG, PW_CHANNEL.
// Output: <OUT>/00-setup.mp4, 01-admin.mp4 … 05-manager.mp4, HotelPOS-full-walkthrough-<lang>.mp4, narration-script.md.

import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { CSS, esc, renderFrames, repo, here, clipStill, clipLive, concat, duration, fmt, run } from './demo-video-lib.mjs';

const LANG = process.env.LANG_ID ?? 'en';
// JSON written by PowerShell carries a byte-order mark, which JSON.parse rejects.
const readJson = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^﻿/, ''));
const N = readJson(path.join(here, 'i18n', `narration.${LANG}.json`));
const OUT = process.env.OUT_DIR ?? path.join(repo, 'artifacts', 'demo-video-i18n', LANG);
const WORK = path.join(OUT, 'work');
// Recordings: <OUT>/record when it exists (fresh runs recorded for this video), else the demo-video-screen pack.
const RECORD = process.env.RECORD_DIR ?? (fs.existsSync(path.join(OUT, 'record')) ? path.join(OUT, 'record') : path.join(repo, 'artifacts', 'demo-video-screen', 'record'));
const UIUX = path.join(repo, 'docs', 'ui-ux');
const MAX_LEAD = 8;
fs.mkdirSync(WORK, { recursive: true });

// English role-step lines come from the live-video narration unless the language file overrides them.
const english = readJson(path.join(here, 'demo-narration.json'));
delete english._comment;
const stepText = key => (N.steps && key in N.steps) ? N.steps[key] : (LANG === 'en' ? english[key] : undefined);
const wavId = key => key.replace(/\//g, '--');

// ---------------------------------------------------------------- text helpers

// On-screen caption of a narration line (identical to the spoken text; a language file may override per line later).
const caption = t => t;

// What the voice reads: abbreviations spelt out in the script's own letters.
function speechOf(t) {
  let s = String(t).replace(/₹/g, N.ui.rupee + ' ').replace(/×/g, ' x ').replace(/→/g, ' - ').replace(/…/g, '.');
  for (const [abbr, said] of Object.entries(N.ui.spell ?? {})) s = s.replace(new RegExp(`\\b${abbr}\\b`, 'g'), said);
  return s.replace(/\s+/g, ' ').trim();
}

// ---------------------------------------------------------------- 1. speech (edge-tts, one mp3 per line)

function speak(lines) {
  const todo = lines.filter(l => !fs.existsSync(path.join(WORK, `${l.id}.mp3`)));
  if (todo.length) {
    const file = path.join(WORK, 'lines.json');
    fs.writeFileSync(file, JSON.stringify(todo.map(l => ({ id: l.id, text: l.text })), null, 1), 'utf8');
    // EDGE_TTS_PY = a python.exe that has edge-tts installed; otherwise a venv next to the output (artifacts/demo-video-i18n/tts-venv), else python on PATH.
    const venvs = [path.join(path.dirname(OUT), 'tts-venv'), path.join(repo, 'artifacts', 'demo-video-i18n', 'tts-venv')].map(v => path.join(v, 'Scripts', 'python.exe'));
    const py = process.env.EDGE_TTS_PY ?? (venvs.find(v => fs.existsSync(v)) ?? 'python');
    const r = spawnSync(py, ['-I', path.join(here, 'demo-video-edge-tts.py'), file, WORK, N.voice, N.rate ?? '+0%'], { encoding: 'utf8', stdio: 'inherit' });
    if (r.status !== 0) throw new Error(`edge-tts failed (${py}); install it with "python -m pip install edge-tts" or set EDGE_TTS_PY`);
  }
  const seconds = {};
  for (const l of lines) seconds[l.id] = duration(path.join(WORK, `${l.id}.mp3`));
  return seconds;
}

// ---------------------------------------------------------------- 2. frames

const CSS2 = CSS + `
  .brand, .chip, .text, .card h1, .card h2, .card li, .foot, .setup { font-family: 'Segoe UI', 'Nirmala UI', Inter, system-ui, sans-serif; }
  .setup { position: absolute; left: 0; right: 0; top: 84px; bottom: 176px; display: flex; gap: 48px; padding: 56px 96px; align-items: center; }
  .setup .left { flex: 1 1 0; min-width: 0; }
  .setup .right { flex: 0 0 760px; display: flex; align-items: center; justify-content: center; }
  .setup.wide .right { flex: 0 0 1080px; }
  .cap .text.long { font-size: 29px; line-height: 1.28; }
  .cap .text.xlong { font-size: 25px; line-height: 1.25; }
  .setup .right img { max-width: 760px; max-height: 640px; border-radius: 12px; box-shadow: 0 24px 64px rgba(0,0,0,.55); border: 1px solid #334155; }
  .setup h1 { font-size: 64px; font-weight: 800; color: #f9fafb; letter-spacing: -.5px; line-height: 1.15; }
  .setup h2 { font-size: 32px; font-weight: 500; color: #94a3b8; margin: 14px 0 36px; }
  .setup li { list-style: none; font-size: 32px; line-height: 1.35; margin: 12px 0; padding-left: 46px; position: relative; color: #e2e8f0; }
  .setup li::before { content: ''; position: absolute; left: 0; top: 14px; width: 20px; height: 20px; border-radius: 6px; background: #22c55e; }
  .setup .code { margin-top: 28px; background: #020617; border: 1px solid #1e293b; border-radius: 12px; padding: 20px 26px; font-family: Consolas, 'Cascadia Mono', monospace;
    font-size: 27px; color: #bbf7d0; white-space: pre-wrap; word-break: break-all; }
  .setup .boxes { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }
  .setup .box { background: #111827; border: 1px solid #1f2937; border-radius: 14px; padding: 22px 26px; }
  .setup .box b { display: block; font-size: 30px; color: #f9fafb; margin-bottom: 6px; }
  .setup .box span { font-size: 24px; color: #94a3b8; line-height: 1.3; }
`;

const img64 = rel => fs.readFileSync(path.join(UIUX, rel)).toString('base64');

function cardHtml(c) {
  return `<!doctype html><html lang="${LANG}"><head><meta charset="utf-8"><style>${CSS2}</style></head><body>
    <div class="top"><div class="brand">Hotel<b>POS</b></div><div class="chip">${esc(c.chipText ?? N.ui.walkthrough)}</div></div>
    <div class="card"><h1>${esc(c.heading)}</h1><h2>${esc(c.sub)}</h2><ul>${c.bullets.map(b => `<li>${esc(caption(b))}</li>`).join('')}</ul></div>
    <div class="foot"><div>${esc(c.foot ?? '')}</div><div>${esc(N.ui.captured)}</div></div>
  </body></html>`;
}

function frameHtml(ch, stepNo, text) {
  return `<!doctype html><html lang="${LANG}"><head><meta charset="utf-8"><style>${CSS2}</style></head><body>
    <div class="top"><div class="brand">Hotel<b>POS</b></div>
      <div class="chip">${esc(ch.title)} <span>${esc(ch.role)}</span> ${esc(ch.client)}</div></div>
    <div class="stage"></div>
    ${capHtml(stepNo, text)}
  </body></html>`;
}

// Caption bar; long narration lines get a smaller font so they stay inside the bar.
function capHtml(stepNo, text) {
  const t = caption(text);
  const cls = t.length > 330 ? ' xlong' : t.length > 210 ? ' long' : '';
  return `<div class="cap"><div class="step">${stepNo}</div><div class="text${cls}">${esc(t)}</div></div>`;
}

// A set-up step: heading, bullets and either a screenshot, a command line or a 2x2 grid of boxes on the right.
function setupHtml(ch, stepNo, s) {
  let right = '';
  if (s.image) right = `<img src="data:image/png;base64,${img64(s.image)}">`;
  else if (s.boxes) right = `<div class="boxes">${s.boxes.map(b => `<div class="box"><b>${esc(caption(b[0]))}</b><span>${esc(caption(b[1]))}</span></div>`).join('')}</div>`;
  return `<!doctype html><html lang="${LANG}"><head><meta charset="utf-8"><style>${CSS2}</style></head><body>
    <div class="top"><div class="brand">Hotel<b>POS</b></div>
      <div class="chip">${esc(ch.title)} <span>${esc(ch.role)}</span> ${esc(ch.client)}</div></div>
    <div class="setup${s.boxes && !s.bullets ? ' wide' : ''}"><div class="left"><h1>${esc(caption(s.heading))}</h1>${s.sub ? `<h2>${esc(caption(s.sub))}</h2>` : ''}
      <ul>${(s.bullets ?? []).map(b => `<li>${esc(caption(b))}</li>`).join('')}</ul>${s.code ? `<div class="code">${esc(s.code)}</div>` : ''}</div>
      ${right ? `<div class="right">${right}</div>` : ''}</div>
    ${capHtml(stepNo, s.speech)}
  </body></html>`;
}

// ---------------------------------------------------------------- 3. chapters

function desktopSteps(folder) {
  const file = path.join(RECORD, folder + '.json');
  if (!fs.existsSync(file)) return null;
  const rec = readJson(file);
  return rec.steps.filter(s => s.included && stepText(s.key)).map(s => ({ ...s, src: path.join(RECORD, rec.video) }));
}
function webSteps() {
  const file = path.join(RECORD, 'web-record.json');
  if (!fs.existsSync(file)) return { '2': null, '3': null };
  const rec = readJson(file);
  const all = rec.steps.map(s => ({ ...s, src: rec.videos[s.role], crop: s.role === 'waiter' ? '390:844:0:0' : null }));
  const first = all.findIndex(s => /^Kitchen/.test(s.chapter));
  let last = -1;
  all.forEach((s, i) => { if (/^Waiter: ready/.test(s.chapter)) last = i; });
  const inKitchen = i => first >= 0 && i >= first && i <= (last >= 0 ? last : all.length - 1);
  const ok = s => s.included && stepText(s.key);
  return { '2': all.filter((s, i) => ok(s) && !inKitchen(i)), '3': all.filter((s, i) => ok(s) && inKitchen(i)) };
}

// --prepare: speak the role-step lines and write work/durations.json for the recording harnesses (they pause after each
// step for the length of its narration), then stop. Same contract as demo-video-live.mjs --prepare.
if (process.argv.includes('--prepare')) {
  const keys = Object.keys(LANG === 'en' ? english : N.steps).filter(k => stepText(k));
  const sec = speak(keys.map(k => ({ id: wavId(k), text: speechOf(stepText(k)) })));
  const durations = {};
  for (const k of keys) durations[k] = sec[wavId(k)];
  fs.writeFileSync(path.join(WORK, 'durations.json'), JSON.stringify(durations, null, 1));
  console.log(`${keys.length} lines spoken; durations -> ${path.join(WORK, 'durations.json')}\nrecord into ${RECORD}`);
  process.exit(0);
}

const web = webSteps();
const chapters = [];
// Chapter 0: set-up. Preferred: a screen recording <RECORD>/00-setup.json + .mp4 (record-screen.ps1 writes the step marks;
// step ids match N.setup.steps so the narration comes from there). Fallback with SETUP_SLIDES=1: designed frames. Else: no chapter 0.
const setupRec = path.join(RECORD, '00-setup.json');
if (fs.existsSync(setupRec)) {
  const rec = readJson(setupRec);
  const byId = Object.fromEntries(N.setup.steps.map(s => [s.id, s]));
  const steps = rec.steps.filter(s => s.included !== false && (byId[s.id]?.speech || s.caption))
    .map((s, i) => ({ ...s, no: i + 1, key: `00-setup/${s.id}`, step: byId[s.id]?.heading ?? s.id, caption: byId[s.id]?.speech ?? s.caption, src: path.join(RECORD, rec.video) }));
  chapters.push({ n: '0', file: '00-setup', ...N.setup, kind: 'live', steps });
} else if (process.env.SETUP_SLIDES === '1') {
  chapters.push({ n: '0', file: '00-setup', ...N.setup, kind: 'setup', steps: N.setup.steps.map((s, i) => ({ ...s, no: i + 1, key: `00-setup/${s.id}` })) });
} else console.log(`chapter 0 (set-up): no ${setupRec} and SETUP_SLIDES is not 1, skipped`);
for (const n of ['1', '2', '3', '4', '5']) {
  const r = N.roles[n];
  const steps = n === '2' || n === '3' ? web[n] : desktopSteps(r.folder);
  if (!steps) { console.log(`chapter ${n}: no recording under ${RECORD}, skipped`); continue; }
  chapters.push({ n, file: r.file, ...r, kind: 'live', steps: steps.map((s, i) => ({ ...s, no: i + 1, caption: stepText(s.key) })) });
}

// ---------------------------------------------------------------- 4. speech lines, frames, clips

// Without a set-up chapter the opening must not promise one: N.openingRoles (when present) replaces N.opening.
if (!chapters.some(ch => ch.n === '0') && N.openingRoles) Object.assign(N.opening, N.openingRoles);

const cardOf = ch => ({
  id: `${ch.file}-00-card`, heading: ch.role, sub: `${ch.title} · ${ch.client}`, bullets: ch.duties, chipText: N.ui.chapter.replace('{n}', ch.n),
  foot: N.ui.steps.replace('{n}', ch.steps.length), speech: ch.speech,
});
const lines = [
  { id: 'opening', text: speechOf(N.opening.speech) }, { id: 'closing', text: speechOf(N.closing.speech) },
];
for (const ch of chapters) {
  ch.card = cardOf(ch);
  lines.push({ id: ch.card.id, text: speechOf(ch.card.speech) });
  for (const s of ch.steps) lines.push({ id: wavId(s.key), text: speechOf(s.kind === 'setup' || ch.kind === 'setup' ? s.speech : s.caption) });
}
const seconds = speak(lines);

const frames = [
  { png: path.join(WORK, 'opening.png'), html: cardHtml({ ...N.opening, chipText: N.ui.walkthrough }) },
  { png: path.join(WORK, 'closing.png'), html: cardHtml({ ...N.closing, chipText: N.ui.walkthrough }) },
];
for (const ch of chapters) {
  frames.push({ png: path.join(WORK, `${ch.card.id}.png`), html: cardHtml(ch.card) });
  for (const s of ch.steps) {
    s.png = path.join(WORK, `${wavId(s.key)}.png`);
    frames.push({ png: s.png, html: ch.kind === 'setup' ? setupHtml(ch, s.no, s) : frameHtml(ch, s.no, s.caption) });
  }
}
console.log(`rendered ${await renderFrames(frames)} new frames`);

let encoded = 0;
const still = (id, pause) => { const mp4 = path.join(WORK, `${id}.mp4`); if (clipStill(path.join(WORK, `${id}.png`), path.join(WORK, `${id}.mp3`), mp4, pause)) encoded++; return mp4; };
const script = [`# HotelPOS full-flow walkthrough — narration script (${N.language})`, ''];
const summary = [];
const fullList = [still('opening')];
for (const ch of chapters) {
  const clips = [still(ch.card.id)];
  for (const s of ch.steps) {
    const id = wavId(s.key);
    if (ch.kind === 'setup') { clips.push(still(id, 1.2)); continue; }
    const speech = seconds[id];
    let start = s.start, len = s.end - s.start;
    if (len > speech + MAX_LEAD) { start = s.end - (speech + MAX_LEAD); len = speech + MAX_LEAD; }
    if (len < speech + 0.5) len = speech + 0.5;
    const mp4 = path.join(WORK, `live-${id}.mp4`);
    if (clipLive(s.src, Math.max(0, start), len, s.png, path.join(WORK, `${id}.mp3`), mp4, s.crop)) encoded++;
    clips.push(mp4);
  }
  const out = path.join(OUT, `${ch.file}.mp4`);
  concat(clips, out);
  fullList.push(...clips);
  const total = duration(out);
  summary.push(`${path.basename(out).padEnd(16)} ${ch.role.padEnd(16)} ${String(ch.steps.length).padStart(3)} steps  ${fmt(total)}`);
  script.push(`## ${path.basename(out)} — ${ch.role} (${fmt(total)})`, '', `- **card** ${ch.card.speech}`);
  for (const s of ch.steps) script.push(`- **${s.no}. ${s.step ?? s.heading}** ${ch.kind === 'setup' ? s.speech : s.caption}`);
  script.push('');
}
fullList.push(still('closing'));
const full = path.join(OUT, `HotelPOS-full-walkthrough-${LANG}.mp4`);
concat(fullList, full);
script.unshift(`Full walkthrough: ${fmt(duration(full))}. Voice: ${N.voice}.`, '', `Opening: ${N.opening.speech}`, '', `Closing: ${N.closing.speech}`, '');
fs.writeFileSync(path.join(OUT, 'narration-script.md'), script.join('\n'), 'utf8');
console.log(`encoded ${encoded} new clips`);
for (const s of summary) console.log(s);
console.log(`${path.basename(full).padEnd(40)} ${fmt(duration(full))}  (${(fs.statSync(full).size / 1048576).toFixed(0)} MB) -> ${OUT}`);
