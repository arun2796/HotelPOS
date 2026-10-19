// Promo-style demo videos cut from the recorded live runs, in the design of the owner's reference clip: dark navy scenes
// with soft glows, a spaced-caps label, a bold headline with an accent word, the app inside a window or phone mockup,
// the plain-English line as subtitle and voice, and a quiet music bed ducked under the voice.
//
// Needs the recordings and spoken lines that demo-video-live.mjs prepares (steps 1–3 there), then:
//   node demo-video-promo.mjs            -> <OUT>/promo/01-admin.mp4 … 05-manager.mp4, apps/*.mp4, HotelPOS-full-walkthrough.mp4
// Env: OUT_DIR (default <repo>/artifacts/demo-video-screen), MUSIC=<wav/mp3> (default: synthesized bed), MUSIC_LEVEL (0.16),
//      ROLES (chapter numbers), PW_CHANNEL, FFMPEG.

import fs from 'node:fs';
import path from 'node:path';
import { ROLES, OPENING, CLOSING, roleCard, renderFrames, repo, here, speak, speechOf, concat, duration, fmt, FFPROBE, run } from './demo-video-lib.mjs';
import { promoFrameHtml, promoCardHtml, fitBox, clipPromo, clipPromoStill, writeMusic, mixMusic } from './demo-video-promo-lib.mjs';

const OUT = process.env.OUT_DIR ?? path.join(repo, 'artifacts', 'demo-video-screen');
const WORK = path.join(OUT, 'work');
const RECORD = path.join(OUT, 'record');
const PROMO = path.join(OUT, 'promo');
const ONLY = process.env.ROLES ? process.env.ROLES.split(',').map(s => s.trim()) : ['1', '2', '3', '4', '5'];
const MAX_LEAD = 8;
const LEVEL = Number(process.env.MUSIC_LEVEL ?? '0.16');
// THEME colours the role and app videos; FULL_THEME (default: the same) colours the full walkthrough.
const THEME = process.env.THEME ?? 'navy';
const FULL_THEME = process.env.FULL_THEME ?? THEME;
const themes = [...new Set([THEME, FULL_THEME])];
const sfx = t => t === 'navy' ? '' : `-${t}`;

const narration = JSON.parse(fs.readFileSync(path.join(here, 'demo-narration.json'), 'utf8'));
delete narration._comment;
const wavId = key => key.replace(/\//g, '--');
fs.mkdirSync(path.join(PROMO, 'apps'), { recursive: true });

// ---------------------------------------------------------------- speech (cached by demo-video-live.mjs --prepare)

const cards = { opening: OPENING, closing: CLOSING };
for (const n of Object.keys(ROLES)) cards[n] = roleCard(n);
const APPS = {
  desktop: { id: 'app-desktop', file: 'HotelPOS-Desktop-app', eyebrow: 'Windows desktop app', headline: 'The desktop app', chapters: ['1', '4', '5'],
    sub: 'For the administrator, the cashier and the manager: set-up, billing, reports and the audit log on the restaurant\'s Windows terminals.',
    pills: ['Administrator', 'Cashier', 'Manager', 'Works offline', 'Licensed terminals'],
    speech: 'The desktop app. It runs on the restaurant\'s Windows terminals, for the administrator, the cashier and the manager. Set-up, billing, reports, and the audit log.' },
  phone: { id: 'app-phone', file: 'HotelPOS-Phone-app', eyebrow: 'Phone app for waiters', headline: 'The phone app', chapters: ['2'],
    sub: 'Waiters take orders at the table on their own phones: the menu, options and notes, the kitchen ticket, the bill request, and it keeps working when the Wi-Fi drops.',
    pills: ['Any phone', 'Browser or Android', 'Live tables', 'Offline tolerant'],
    speech: 'The phone app. Waiters take orders at the table on their own phones, send them to the kitchen, and ask for the bill. It keeps working when the Wi-Fi drops.' },
  tablet: { id: 'app-tablet', file: 'HotelPOS-Kitchen-tablet-app', eyebrow: 'Kitchen display for tablets', headline: 'The kitchen app', chapters: ['3'],
    sub: 'The kitchen board on a tablet: tickets arrive live, the cook accepts, starts and marks them ready, and the waiter is told at once.',
    pills: ['Any tablet', 'Live tickets', 'Item pictures', 'Per-station view'],
    speech: 'The kitchen app. Tickets arrive live on the tablet. The cook accepts them, starts cooking, and marks them ready, and the waiter is told at once.' },
};
const lines = [
  ...Object.values(cards).map(c => ({ id: c.id, text: c.speech })),
  ...Object.values(APPS).map(a => ({ id: a.id, text: a.speech })),
  ...Object.entries(narration).filter(([, t]) => t).map(([k, t]) => ({ id: wavId(k), text: speechOf(t) })),
];
const seconds = speak(lines, WORK);

// ---------------------------------------------------------------- recorded steps per chapter

function desktopSteps(folder) {
  const file = path.join(RECORD, folder + '.json');
  if (!fs.existsSync(file)) return null;
  const rec = JSON.parse(fs.readFileSync(file, 'utf8'));
  return rec.steps.filter(s => s.included).map(s => ({ ...s, src: path.join(RECORD, rec.video), kind: 'window', srcW: 1820, srcH: 1000 }));
}

function webSteps() {
  const file = path.join(RECORD, 'web-record.json');
  if (!fs.existsSync(file)) return { '2': null, '3': null };
  const rec = JSON.parse(fs.readFileSync(file, 'utf8'));
  const all = rec.steps.map(s => ({ ...s, src: rec.videos[s.role],
    ...(s.role === 'waiter' ? { crop: '390:844:0:0', kind: 'phone', srcW: 390, srcH: 844 } : { crop: null, kind: 'window', srcW: 1024, srcH: 768 }) }));
  const first = all.findIndex(s => /^Kitchen/.test(s.chapter));
  let last = -1;
  all.forEach((s, i) => { if (/^Waiter: ready/.test(s.chapter)) last = i; });
  const inKitchen = i => first >= 0 && i >= first && i <= (last >= 0 ? last : all.length - 1);
  return { '2': all.filter((s, i) => s.included && !inKitchen(i)), '3': all.filter((s, i) => s.included && inKitchen(i)) };
}

// Probe the real size of each source once (the desktop recording width depends on the DPI and window width).
const sizeCache = new Map();
function sizeOf(file) {
  if (!sizeCache.has(file)) {
    const [w, h] = run(FFPROBE, ['-v', 'error', '-select_streams', 'v:0', '-show_entries', 'stream=width,height', '-of', 'csv=p=0', file]).trim().split(',').map(Number);
    sizeCache.set(file, { w, h });
  }
  return sizeCache.get(file);
}

const web = webSteps();
const chapters = [];
for (const n of ONLY) {
  const r = ROLES[n];
  const steps = n === '2' || n === '3' ? web[n] : desktopSteps(r.folders[0]);
  if (!steps) { console.log(`chapter ${n} (${r.role}): no recording found, skipped`); continue; }
  chapters.push({ n, r, steps });
}
if (!chapters.length) throw new Error('nothing recorded under ' + RECORD);

const titleOf = s => s.step.replace(/\s*\(.*?\)\s*/g, ' ').replace(/\s+/g, ' ').trim();

// ---------------------------------------------------------------- frames

const frames = [];
for (const theme of themes) {
  const x = sfx(theme);
  frames.push({ png: path.join(WORK, `promo-opening${x}.png`), html: promoCardHtml({ theme, eyebrow: 'Restaurant point of sale', headline: 'HotelPOS', sub: OPENING.sub, pills: ['Administrator', 'Waiter', 'Kitchen', 'Cashier', 'Manager'] }) });
  frames.push({ png: path.join(WORK, `promo-closing${x}.png`), html: promoCardHtml({ theme, eyebrow: 'What runs underneath', headline: CLOSING.heading, sub: CLOSING.sub, pills: CLOSING.bullets, small: true }) });
  for (const a of Object.values(APPS)) frames.push({ png: path.join(WORK, `promo-${a.id}${x}.png`), html: promoCardHtml({ theme, eyebrow: a.eyebrow, headline: a.headline, sub: a.sub, pills: a.pills }) });
  for (const ch of chapters) {
    const card = cards[ch.n];
    frames.push({ png: path.join(WORK, `promo-${card.id}${x}.png`), html: promoCardHtml({ theme, eyebrow: `Chapter ${ch.n} · ${ch.r.title}`, headline: ch.r.role, sub: `${ch.r.client} · ${ch.steps.length} steps`, pills: ch.r.duties, small: true }) });
    ch.steps.forEach((s, i) => {
      const size = s.crop ? { w: s.srcW, h: s.srcH } : sizeOf(s.src);
      s.no = i + 1;
      s.box = fitBox(size.w, size.h, s.kind);
      s.caption = narration[s.key];
      s.png ??= {};
      s.png[theme] = path.join(WORK, `promo-${wavId(s.key)}${x}.png`);
      frames.push({ png: s.png[theme], html: promoFrameHtml({ theme, eyebrow: `${ch.r.role} · ${ch.r.title}`, headline: titleOf(s), box: s.box, subtitle: s.caption, title: `HotelPOS · ${ch.r.role}` }) });
    });
  }
}
console.log(`rendered ${await renderFrames(frames)} new frames`);

// ---------------------------------------------------------------- clips and outputs

let encoded = 0;
const still = (id, theme) => { const x = sfx(theme); const mp4 = path.join(WORK, `promo-${id}${x}.mp4`); if (clipPromoStill(path.join(WORK, `promo-${id}${x}.png`), path.join(WORK, `${id}.wav`), mp4)) encoded++; return mp4; };
const chapterClips = {};
for (const theme of themes) {
  chapterClips[theme] = {};
  for (const ch of chapters) {
    const clips = [still(cards[ch.n].id, theme)];
    for (const s of ch.steps) {
      const wav = path.join(WORK, `${wavId(s.key)}.wav`);
      const speech = seconds[wavId(s.key)];
      let start = s.start;
      let len = s.end - s.start;
      if (len > speech + MAX_LEAD) { start = s.end - (speech + MAX_LEAD); len = speech + MAX_LEAD; }
      if (len < speech + 0.5) len = speech + 0.5;
      const mp4 = path.join(WORK, `promo-${wavId(s.key)}${sfx(theme)}.mp4`);
      if (clipPromo(s.src, Math.max(0, start), len, s.png[theme], wav, mp4, s.box, s.crop)) encoded++;
      clips.push(mp4);
    }
    chapterClips[theme][ch.n] = clips;
  }
}
console.log(`encoded ${encoded} new clips`);

const music = process.env.MUSIC ?? path.join(WORK, 'music-bed.wav');
const totalLen = Object.values(chapterClips[THEME]).flat().reduce((a, c) => a + duration(c), 0) + 60;
if (!process.env.MUSIC && (!fs.existsSync(music) || duration(music) < totalLen)) { writeMusic(music, totalLen); console.log(`music bed ${fmt(totalLen)}`); }

function finish(clips, name) {
  const raw = path.join(WORK, `promo-raw-${path.basename(name)}`);
  concat(clips, raw);
  mixMusic(raw, music, name, LEVEL);
  fs.unlinkSync(raw);
  return duration(name);
}

const summary = [];
for (const ch of chapters) {
  const out = path.join(PROMO, `${ch.r.file}.mp4`);
  summary.push(`${path.basename(out).padEnd(32)} ${ch.r.role.padEnd(14)} ${String(ch.steps.length).padStart(3)} steps  ${fmt(finish(chapterClips[THEME][ch.n], out))}`);
}
for (const a of Object.values(APPS)) {
  const have = a.chapters.filter(n => chapterClips[THEME][n]);
  if (!have.length) continue;
  const out = path.join(PROMO, 'apps', `${a.file}.mp4`);
  summary.push(`apps/${path.basename(out).padEnd(27)} ${a.headline.padEnd(14)} ${fmt(finish([still(a.id, THEME), ...have.flatMap(n => chapterClips[THEME][n])], out))}`);
}
const full = path.join(PROMO, 'HotelPOS-full-walkthrough.mp4');
const fullLen = finish([still('opening', FULL_THEME), ...chapters.flatMap(ch => chapterClips[FULL_THEME][ch.n]), still('closing', FULL_THEME)], full);
for (const s of summary) console.log(s);
console.log(`${path.basename(full).padEnd(32)} ${fmt(fullLen)}  (${(fs.statSync(full).size / 1048576).toFixed(0)} MB, theme ${FULL_THEME}) -> ${PROMO}`);
