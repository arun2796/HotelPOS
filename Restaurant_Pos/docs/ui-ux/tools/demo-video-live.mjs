// Narrated demo videos cut from RECORDED LIVE RUNS of the real application (no still pictures): one MP4 per role and
// one full walkthrough. The desktop harness (LiveFlowScreenshots.Record.cs) and the web flow (demo-web-flow.mjs with
// RECORD_DIR) record the screens while they drive the app and write the second at which every step starts and ends;
// this script cuts those videos by step, frames each cut on the 1920x1080 canvas with a plain-English caption, lays
// the spoken caption under it, and joins the steps per chapter. Narration: demo-narration.json.
//
// Steps:
//   1. node demo-video-live.mjs --prepare          speaks every line, writes <OUT>/work/durations.json
//   2. desktop run with HOTELPOS_DEMO_RECORD=<OUT>/record HOTELPOS_DEMO_NARRATION=<OUT>/work/durations.json
//      (plus the usual HOTELPOS_LIVE_SCREENS=1 HOTELPOS_LIVE_SCREENS_ROLES=demo … dotnet test … --filter LiveFlowScreenshots)
//   3. web run with RECORD_DIR=<OUT>/record HOTELPOS_DEMO_NARRATION=<OUT>/work/durations.json node demo-web-flow.mjs
//   4. node demo-video-live.mjs                    cuts and joins: 01-admin.mp4 … 05-manager.mp4, HotelPOS-full-walkthrough.mp4
// Run from a scratch folder that has Playwright installed (the same one as demo-web-flow.mjs).
// Env: OUT_DIR (default <repo>/artifacts/demo-video-live), VOICE, RATE, FFMPEG, PW_CHANNEL, ROLES (chapter numbers).

import fs from 'node:fs';
import path from 'node:path';
import { ROLES, OPENING, CLOSING, roleCard, cardHtml, frameHtml, renderFrames, repo, here, speak, speechOf,
  clipStill, clipLive, concat, duration, fmt } from './demo-video-lib.mjs';

const OUT = process.env.OUT_DIR ?? path.join(repo, 'artifacts', 'demo-video-live');
const WORK = path.join(OUT, 'work');
const RECORD = path.join(OUT, 'record');
const PREPARE = process.argv.includes('--prepare');
const ONLY = process.env.ROLES ? process.env.ROLES.split(',').map(s => s.trim()) : ['1', '2', '3', '4', '5'];
const MAX_LEAD = 8; // seconds of a step shown before its narration ends; longer steps are cut from the end backwards

const narration = JSON.parse(fs.readFileSync(path.join(here, 'demo-narration.json'), 'utf8'));
delete narration._comment;
const wavId = key => key.replace(/\//g, '--');

// ---------------------------------------------------------------- 1. speech

fs.mkdirSync(WORK, { recursive: true });
const cards = { opening: OPENING, closing: CLOSING };
for (const n of Object.keys(ROLES)) cards[n] = roleCard(n);
const lines = [
  ...Object.values(cards).map(c => ({ id: c.id, text: c.speech })),
  ...Object.entries(narration).filter(([, t]) => t).map(([k, t]) => ({ id: wavId(k), text: speechOf(t) })),
];
const seconds = speak(lines, WORK);
const durations = {};
for (const [k, t] of Object.entries(narration)) if (t) durations[k] = seconds[wavId(k)];
fs.writeFileSync(path.join(WORK, 'durations.json'), JSON.stringify(durations, null, 1));
if (PREPARE) {
  console.log(`${lines.length} lines spoken; durations for ${Object.keys(durations).length} steps -> ${path.join(WORK, 'durations.json')}`);
  console.log(`record into ${RECORD}`);
  process.exit(0);
}

// ---------------------------------------------------------------- 2. the recorded steps per chapter

function desktopSteps(folder) {
  const file = path.join(RECORD, folder + '.json');
  if (!fs.existsSync(file)) return null;
  const rec = JSON.parse(fs.readFileSync(file, 'utf8'));
  return rec.steps.filter(s => s.included).map(s => ({ ...s, src: path.join(RECORD, rec.video) }));
}

function webSteps() {
  const file = path.join(RECORD, 'web-record.json');
  if (!fs.existsSync(file)) return { '2': null, '3': null };
  const rec = JSON.parse(fs.readFileSync(file, 'utf8'));
  // the waiter page is a 390x844 phone viewport recorded into a 780x1688 frame: Playwright pads instead of scaling
  const all = rec.steps.map(s => ({ ...s, src: rec.videos[s.role], crop: s.role === 'waiter' ? '390:844:0:0' : null }));
  // Chapter 3 is the kitchen story: from the kitchen sign-in to the waiter serving and asking for the bill.
  const first = all.findIndex(s => /^Kitchen/.test(s.chapter));
  let last = -1;
  all.forEach((s, i) => { if (/^Waiter: ready/.test(s.chapter)) last = i; });
  const inKitchen = (i) => first >= 0 && i >= first && i <= (last >= 0 ? last : all.length - 1);
  return {
    '2': all.filter((s, i) => s.included && !inKitchen(i)),
    '3': all.filter((s, i) => s.included && inKitchen(i)),
  };
}

const web = webSteps();
const chapters = [];
for (const n of ONLY) {
  const r = ROLES[n];
  const steps = n === '2' || n === '3' ? web[n] : desktopSteps(r.folders[0]);
  if (!steps) { console.log(`chapter ${n} (${r.role}): no recording found, skipped`); continue; }
  if (!steps.length) throw new Error(`chapter ${n}: the recording has no narrated steps`);
  chapters.push({ n, r, steps });
}
if (!chapters.length) throw new Error('nothing recorded under ' + RECORD);

// ---------------------------------------------------------------- 3. frames, clips, chapters

const frames = [{ png: path.join(WORK, 'opening.png'), html: cardHtml(OPENING) }, { png: path.join(WORK, 'closing.png'), html: cardHtml(CLOSING) }];
for (const ch of chapters) {
  frames.push({ png: path.join(WORK, `${cards[ch.n].id}.png`), html: cardHtml({ ...cards[ch.n], foot: `${ch.steps.length} steps` }) });
  ch.steps.forEach((s, i) => { s.no = i + 1; s.caption = narration[s.key]; s.png = path.join(WORK, `live-${wavId(s.key)}.png`); frames.push({ png: s.png, html: frameHtml(ch.n, s.no, s.caption) }); });
}
console.log(`rendered ${await renderFrames(frames)} new frames`);

let encoded = 0;
const still = (id) => { const mp4 = path.join(WORK, `${id}.mp4`); if (clipStill(path.join(WORK, `${id}.png`), path.join(WORK, `${id}.wav`), mp4)) encoded++; return mp4; };
const script = ['# HotelPOS live demo videos — narration script', ''];
const summary = [];
const fullList = [still('opening')];
for (const ch of chapters) {
  const clips = [still(cards[ch.n].id)];
  for (const s of ch.steps) {
    const wav = path.join(WORK, `${wavId(s.key)}.wav`);
    const speech = seconds[wavId(s.key)];
    let start = s.start;
    let len = s.end - s.start;
    if (len > speech + MAX_LEAD) { start = s.end - (speech + MAX_LEAD); len = speech + MAX_LEAD; }
    if (len < speech + 0.5) len = speech + 0.5; // the recording ended early (last step): hold the last frame via the overlay loop
    const mp4 = path.join(WORK, `live-${wavId(s.key)}.mp4`);
    if (clipLive(s.src, Math.max(0, start), len, s.png, wav, mp4, s.crop)) encoded++;
    clips.push(mp4);
  }
  const out = path.join(OUT, `${ch.r.file}.mp4`);
  concat(clips, out);
  fullList.push(...clips);
  const total = duration(out);
  summary.push(`${path.basename(out).padEnd(16)} ${ch.r.role.padEnd(14)} ${String(ch.steps.length).padStart(3)} steps  ${fmt(total)}`);
  script.push(`## ${path.basename(out)} — ${ch.r.role} (${fmt(total)})`, '', `- **card** ${cards[ch.n].speech}`);
  for (const s of ch.steps) script.push(`- **${s.no}. ${s.step}** ${s.caption}`);
  script.push('');
}
fullList.push(still('closing'));
const full = path.join(OUT, 'HotelPOS-full-walkthrough.mp4');
concat(fullList, full);
script.unshift(`Full walkthrough: ${fmt(duration(full))}.`, '', `Opening: ${OPENING.speech}`, '', `Closing: ${CLOSING.speech}`, '');
fs.writeFileSync(path.join(OUT, 'narration-script.md'), script.join('\n'));
console.log(`encoded ${encoded} new clips`);
for (const s of summary) console.log(s);
console.log(`${path.basename(full).padEnd(31)} ${fmt(duration(full))}  (${(fs.statSync(full).size / 1048576).toFixed(0)} MB) -> ${OUT}`);
