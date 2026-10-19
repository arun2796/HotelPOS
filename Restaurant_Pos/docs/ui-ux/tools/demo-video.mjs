// Narrated SLIDESHOW videos from the demo pack (docs/demo): one MP4 per role plus one full walkthrough. For videos cut
// from recorded live runs of the application see demo-video-live.mjs.
//
// Each storyboard row in docs/demo/README.md (| # | picture | narration |) becomes one slide: the screenshot framed on
// the 1920x1080 canvas with the narration as caption, spoken by a Windows voice (demo-video-tts.ps1, offline). Slide
// length = speech length + a short pause. Every role video opens with a card that names the role and its
// responsibilities; the full video adds an opening and a closing card.
//
// Output (OUT_DIR, default <repo>/artifacts/demo-video, gitignored): 01-admin.mp4 … 05-manager.mp4,
// HotelPOS-full-walkthrough.mp4, narration-script.md, work/ (frames, WAVs, clips; delete to rebuild everything).
// Run from a scratch folder that has Playwright installed:  node <repo>/docs/ui-ux/tools/demo-video.mjs
// Env: OUT_DIR, DEMO_DIR (default <repo>/docs/demo), VOICE, RATE, FFMPEG, PW_CHANNEL, ROLES (chapter numbers), PAUSE.

import fs from 'node:fs';
import path from 'node:path';
import { ROLES, OPENING, CLOSING, roleCard, cardHtml, frameHtml, renderFrames, repo, speak, speechOf, clipStill, concat, duration, fmt } from './demo-video-lib.mjs';

const DEMO = process.env.DEMO_DIR ?? path.join(repo, 'docs', 'demo');
const OUT = process.env.OUT_DIR ?? path.join(repo, 'artifacts', 'demo-video');
const WORK = path.join(OUT, 'work');
const PAUSE = Number(process.env.PAUSE ?? '0.8');
const ONLY = process.env.ROLES ? process.env.ROLES.split(',').map(s => s.trim()) : null;

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
const slideSpeech = t => speechOf(captionOf(t)).replace(/\bTA-\./g, 'T A').replace(/\bINV-\S+/g, m => m.replace(/-/g, ' '))
  .replace(/\bT0?(\d+)\b/g, 'table $1').replace(/\bPhase \d+\b/g, 'the latest release');

const t0 = Date.now();
fs.mkdirSync(WORK, { recursive: true });
const chapters = parseStoryboard().filter(c => !ONLY || ONLY.includes(c.num));
if (!chapters.length) throw new Error('no chapters parsed from ' + path.join(DEMO, 'README.md'));

// Every item: a card or a slide, with its id (= file stem), html and speech.
const items = [{ id: OPENING.id, html: cardHtml(OPENING), speech: OPENING.speech }];
for (const ch of chapters) {
  const card = roleCard(ch.num, ch.slides.length);
  items.push({ id: card.id, html: cardHtml(card), speech: card.speech, chapter: ch.num });
  for (const s of ch.slides) {
    const png = path.join(DEMO, ch.folder, s.file);
    if (!fs.existsSync(png)) throw new Error('missing screenshot ' + png);
    items.push({ id: `${ROLES[ch.num].file}-${String(s.n).padStart(2, '0')}`, chapter: ch.num, speech: slideSpeech(s.text),
      html: () => frameHtml(ch.num, s.n, captionOf(s.text), fs.readFileSync(png).toString('base64')) });
  }
}
items.push({ id: CLOSING.id, html: cardHtml(CLOSING), speech: CLOSING.speech });

speak(items.map(i => ({ id: i.id, text: i.speech })), WORK);
console.log(`rendered ${await renderFrames(items.map(i => ({ png: path.join(WORK, `${i.id}.png`), html: i.html })))} new frames of ${items.length}`);
let encoded = 0;
for (const it of items) if (clipStill(path.join(WORK, `${it.id}.png`), path.join(WORK, `${it.id}.wav`), path.join(WORK, `${it.id}.mp4`), PAUSE)) encoded++;
console.log(`encoded ${encoded} new clips`);

const script = ['# HotelPOS demo videos — narration script', ''];
const fullList = [path.join(WORK, `${OPENING.id}.mp4`)];
for (const ch of chapters) {
  const r = ROLES[ch.num];
  const clips = items.filter(i => i.chapter === ch.num).map(i => path.join(WORK, `${i.id}.mp4`));
  const out = path.join(OUT, `${r.file}.mp4`);
  concat(clips, out);
  fullList.push(...clips);
  const len = duration(out);
  console.log(`${path.basename(out).padEnd(16)} ${r.role.padEnd(14)} ${String(ch.slides.length).padStart(3)} screens  ${fmt(len)}`);
  script.push(`## ${path.basename(out)} — ${r.role} (${fmt(len)})`, '');
  for (const i of items.filter(i => i.chapter === ch.num)) script.push(`- **${i.id}** ${i.speech}`);
  script.push('');
}
fullList.push(path.join(WORK, `${CLOSING.id}.mp4`));
const full = path.join(OUT, 'HotelPOS-full-walkthrough.mp4');
concat(fullList, full);
script.unshift(`Full walkthrough: ${fmt(duration(full))}.`, '', `Opening: ${OPENING.speech}`, '', `Closing: ${CLOSING.speech}`, '');
fs.writeFileSync(path.join(OUT, 'narration-script.md'), script.join('\n'));
console.log(`${path.basename(full).padEnd(31)} ${fmt(duration(full))}  (${(fs.statSync(full).size / 1048576).toFixed(0)} MB)`);
console.log(`done in ${((Date.now() - t0) / 1000).toFixed(0)}s -> ${OUT}`);
