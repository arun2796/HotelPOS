// HotelPOS web client: DEMO PACK flow (waiter on a phone, kitchen on a tablet) -> numbered screenshots + test record.
// Output (OUT_DIR, default <repo>/docs/demo):
//   02-waiter-phone/NN-name.png     390x844, waiter1
//   03-kitchen-tablet/NN-name.png   1024x768, kitchen1
//   web-test-record.md              one table row per step (Pass/Fail), summary line, UI/UX issues list
//   web-console-errors.json, web-small-targets.json (visible controls under 48 px per screenshot), web-page-overflow.json
// Based on web-flow.mjs (same helpers and Phase 21/22 selectors); extended for the Phase 22 features: List/Map toggle, reservation
// badge, order type chips, takeaway TA-nnnn, held drafts, Print KOT, kitchen item pictures.
//
// How to run (Playwright is NOT in the repo): in a scratch folder OUTSIDE the repo run  npm init -y && npm i playwright
// then, from that folder:  node <repo>/docs/ui-ux/tools/demo-web-flow.mjs   (installed Edge via channel 'msedge').
// Env: HOTELPOS_URL (default http://localhost:5000/app/), HOTELPOS_TABLE (default T04; if it is not free the first free
//      table is used), HOTELPOS_SPARE_TABLE (default T03; only for the offline drill, falls back to another free table),
//      PW_CHANNEL, OUT_DIR, STATE_DIR (default <tmp>/hotelpos-web-flow: keeps the browser device identity = licence seat).
// Accounts: waiter1 / Pass@123, kitchen1 / Pass@123; admin / Admin@1234 (ADMIN_PASSWORD) only for the API set-up
// (demo pictures) and for deactivating the WEB-* terminals this run used (set KEEP_DEVICES=1 to skip that).
// Data it creates (owner's dev DB): a reservation for the main table, demo pictures on the menu items it orders (only
// where none exists), a dine-in, a takeaway and a resumed held takeaway order. It deletes nothing.
import { createRequire } from 'node:module';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import os from 'node:os';

const require = createRequire(path.join(process.cwd(), 'noop.js'));
const { chromium } = require('playwright');

const here = path.dirname(fileURLToPath(import.meta.url));
const BASE = process.env.HOTELPOS_URL ?? 'http://localhost:5000/app/';
const API = new URL(BASE).origin;
let TABLE = process.env.HOTELPOS_TABLE ?? 'T04';
let SPARE = process.env.HOTELPOS_SPARE_TABLE ?? 'T03';
const OUT = process.env.OUT_DIR ?? path.resolve(here, '..', '..', 'demo');
const CHANNEL = process.env.PW_CHANNEL ?? 'msedge';
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD ?? 'Admin@1234';
const MIN_TARGET = 48;
const WF = '02-waiter-phone';
const KF = '03-kitchen-tablet';

// ---------------- video recording (RECORD_DIR) ----------------
// With RECORD_DIR set, both pages are recorded (Playwright recordVideo, waiter 780x1688, kitchen 1024x768), a pointer
// is drawn where the script taps, and after every step the run pauses for the step's narration
// (HOTELPOS_DEMO_NARRATION = JSON { "<folder>/<step slug>": seconds }) so the result stays on screen while the voice
// explains it. RECORD_DIR/web-record.json lists the two video files and every step with its start/end second in its
// page's video; docs/ui-ux/tools/demo-video-live.mjs cuts the chapters from that.
const RECORD = process.env.RECORD_DIR ?? null;
const NARRATION = (() => { const f = process.env.HOTELPOS_DEMO_NARRATION; return f && fs.existsSync(f) ? JSON.parse(fs.readFileSync(f, 'utf8')) : {}; })();
const stepKey = (folder, name) => folder + '/' + name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
const marks = [];
const videoT0 = {};
const POINTER = `(() => { if (window.top !== window) return; const d = document.createElement('div');
  d.style.cssText = 'position:fixed;z-index:2147483647;width:26px;height:26px;margin:-13px 0 0 -13px;pointer-events:none;border-radius:50%;background:rgba(220,38,38,.55);border:3px solid #fff;box-shadow:0 0 0 2px rgba(0,0,0,.35);transition:transform .08s;display:none';
  const add = () => document.documentElement.appendChild(d);
  document.readyState === 'loading' ? document.addEventListener('DOMContentLoaded', add) : add();
  const mv = e => { d.style.display = 'block'; d.style.left = e.clientX + 'px'; d.style.top = e.clientY + 'px'; };
  document.addEventListener('mousemove', mv, true);
  document.addEventListener('mousedown', e => { mv(e); d.style.transform = 'scale(.6)'; }, true);
  document.addEventListener('mouseup', () => { d.style.transform = 'scale(1)'; }, true);
  document.addEventListener('touchstart', e => { const t = e.touches[0]; if (t) mv(t); }, true); })();`;
for (const f of [WF, KF]) {
  fs.rmSync(path.join(OUT, f), { recursive: true, force: true });
  fs.mkdirSync(path.join(OUT, f), { recursive: true });
}
const STATE_DIR = process.env.STATE_DIR ?? path.join(os.tmpdir(), 'hotelpos-web-flow');
fs.mkdirSync(STATE_DIR, { recursive: true });
const stateFile = role => path.join(STATE_DIR, role + '.json');
const stateFor = role => (fs.existsSync(stateFile(role)) ? stateFile(role) : undefined);
async function saveState(ctx, role) {
  const st = await ctx.storageState();
  st.cookies = [];
  fs.writeFileSync(stateFile(role), JSON.stringify(st));
}

const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
const pause = (p, ms = 700) => p.waitForTimeout(ms);

// ---------------- API helpers (set-up only; the screenshots are all taken in the PWA) ----------------
// The dev API sometimes refuses a connection for a few seconds (shared with other sessions); retry before failing.
const nativeFetch = globalThis.fetch;
const fetch = async (...a) => { for (let i = 0; ; i++) { try { return await nativeFetch(...a); } catch (e) { if (i >= 6) throw e; await new Promise(r => setTimeout(r, 4000)); } } };
async function apiLogin(username, password) {
  const r = await fetch(API + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username, password }) });
  const j = await r.json();
  if (!j.success) throw new Error(`API login ${username}: ${j.message}`);
  return j.data.accessToken;
}
const apiGet = async (t, p) => (await fetch(API + p, { headers: { Authorization: 'Bearer ' + t } })).json();
const apiPost = async (t, p, body) => (await fetch(API + p, { method: 'POST', headers: { Authorization: 'Bearer ' + t, 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify(body ?? {}) })).json();

// ---------------- step recorder ----------------
const rows = [];
const small = {};
const overflow = {};
const consoleErrors = [];
const counters = { [WF]: 0, [KF]: 0 };
let stepNo = 0;

async function shot(page, folder, name, opts = {}) {
  await pause(page, opts.wait ?? 800);
  const n = ++counters[folder];
  const file = `${String(n).padStart(2, '0')}-${name}.png`;
  await page.screenshot({ path: path.join(OUT, folder, file), fullPage: !!opts.full });
  if (opts.measure === false) return `${folder}/${file}`;
  const key = `${folder}/${file}`;
  small[key] = await page.evaluate(min => {
    const out = [];
    for (const el of document.querySelectorAll('button, a[href], input, textarea, [role=button]')) {
      const r = el.getBoundingClientRect();
      const st = getComputedStyle(el);
      if (r.width === 0 || r.height === 0 || st.visibility === 'hidden') continue;
      if (r.bottom < 0 || r.top > innerHeight) continue;
      if (el.type === 'checkbox' || r.width < min || r.height < min) {
        out.push({ tag: el.tagName.toLowerCase(), cls: (el.className?.toString() ?? '').slice(0, 40), text: (el.innerText || el.getAttribute('aria-label') || el.type || '').trim().slice(0, 30), w: Math.round(r.width), h: Math.round(r.height) });
      }
    }
    return out;
  }, MIN_TARGET);
  const ov = await page.evaluate(() => {
    const vw = visualViewport.width;
    if (document.documentElement.scrollWidth <= Math.ceil(vw) && innerWidth <= Math.ceil(vw)) return null;
    const wide = [];
    for (const el of document.querySelectorAll('body *')) {
      const r = el.getBoundingClientRect();
      if (r.right > vw + 1 && r.width > 0 && !el.closest('.chip-row, .cart-thumbs, .kds-stations, .floor-map-scroll, .channel-chips')) wide.push(`${el.tagName.toLowerCase()}.${(el.className?.baseVal ?? el.className ?? '').toString().trim().split(/\s+/)[0]} right=${Math.round(r.right)}`);
    }
    return { innerWidth, visualWidth: Math.round(vw), scrollWidth: document.documentElement.scrollWidth, wide: wide.slice(0, 8) };
  });
  if (ov) { overflow[key] = ov; log('  horizontal overflow:', JSON.stringify(ov)); }
  return key;
}

// One recorded step. fn performs the action and asserts the expected state (throws on failure); the screenshot is taken
// afterwards whether or not it passed (a failing screen is evidence too).
async function step(chapter, stepName, action, expected, page, folder, shotName, fn, shotOpts = {}) {
  stepNo++;
  let result = 'Pass';
  let notes = '';
  const t0 = Date.now();
  try {
    let ret;
    // the host's TCP ports are sometimes exhausted by other sessions (ERR_ADDRESS_IN_USE / ERR_NO_BUFFER_SPACE): wait and retry
    for (let attempt = 0; ; attempt++) {
      try { ret = await fn(); break; } catch (e) {
        if (attempt >= 3 || !/net::ERR_(ADDRESS_IN_USE|NO_BUFFER_SPACE|CONNECTION|NETWORK)/.test(e.message)) throw e;
        log('  network error, waiting 30 s before retrying the step:', e.message.split('\n')[0]);
        await new Promise(r => setTimeout(r, 30000));
      }
    }
    if (typeof ret === 'string') notes = ret;
  } catch (e) {
    result = 'Fail';
    notes = String(e.message).split('\n')[0].slice(0, 220);
    log(`  FAIL step ${stepNo} (${stepName}): ${notes}`);
  }
  let file = '';
  if (shotName) {
    try { file = await shot(page, folder, shotName, shotOpts); } catch (e) { notes += ` [screenshot failed: ${String(e.message).split('\n')[0]}]`; }
  }
  rows.push({ n: stepNo, chapter, step: stepName, action, expected, result, file, notes: notes.trim() });
  if (RECORD) {
    const role = page === w ? 'waiter' : 'kitchen';
    const key = stepKey(folder, stepName);
    const included = typeof NARRATION[key] === 'number';
    if (included) await page.waitForTimeout(NARRATION[key] * 1000 + 800).catch(() => {});
    marks.push({ n: stepNo, chapter, step: stepName, key, role, start: (t0 - videoT0[role]) / 1000, end: (Date.now() - videoT0[role]) / 1000, result, included });
  }
  log(`step ${stepNo} ${result} ${stepName}${file ? ' -> ' + file : ''} (${Date.now() - t0} ms)`);
}
const bold = (p, sel, opts) => p.locator(sel).first().waitFor({ timeout: 10000, ...opts });

// ---------------- page helpers (from web-flow.mjs) ----------------
async function clearOverlays(page) {
  for (const t of await page.locator('.toasts .toast').all()) await t.click().catch(() => {});
  const dismiss = page.locator('#blazor-error-ui .dismiss');
  if (await dismiss.isVisible().catch(() => false)) await dismiss.click().catch(() => {});
  await pause(page, 200);
}
async function signIn(page, role, user) {
  await page.goto(BASE + 'signin', { waitUntil: 'domcontentloaded' });
  await page.locator('.signin .card').waitFor();
  await page.locator('.segmented .segment', { hasText: role }).click();
  await page.locator('input[autocomplete=username]').fill(user);
  await page.locator('input[type=password]').fill('Pass@123');
  await pause(page, 300);
}
async function nav(page, label) {
  await clearOverlays(page);
  await page.locator('nav.bottomnav a', { hasText: label }).first().click();
  await pause(page, 900);
}
async function navSheet(page, label) {
  await clearOverlays(page);
  await page.locator('nav.bottomnav button.tabbtn', { hasText: label }).click();
  await page.locator('.overlay .sheet').waitFor();
  await pause(page, 600);
}
async function closeSheet(page) {
  await page.locator('.overlay .sheet .sheet-head button[aria-label="Close"]').first().click();
  await page.locator('.overlay').waitFor({ state: 'detached', timeout: 5000 }).catch(() => {});
  await pause(page, 300);
}
async function kGo(page, label) {
  await clearOverlays(page);
  await page.locator(`nav.headnav a[aria-label="${label}"]`).click();
  await page.locator('.skeleton-group').waitFor({ state: 'detached', timeout: 15000 }).catch(() => {});
  await pause(page, 900);
}
async function openTable(page, code) {
  await nav(page, 'Tables');
  await page.locator('.table-card, [data-testid=floor-tile]').first().waitFor();
  // the Tables page remembers List/Map; the list has .table-card, the map has tiles. Prefer the card; else the tile.
  const card = page.locator('.table-card', { has: page.locator('.table-code', { hasText: new RegExp(`^${code}$`) }) });
  if (await card.count()) await card.first().click();
  else await page.locator('[data-testid=floor-tile]', { has: page.locator('.table-code', { hasText: new RegExp(`^${code}$`) }) }).first().click();
  await page.locator('.detail-card h1').first().waitFor();
  await pause(page, 900);
}
async function action(page, key) {
  await clearOverlays(page);
  await page.locator(`.action-list [data-action="${key}"]`).click();
}
async function satisfyPicker(page) {
  const groups = page.locator('.sheet .options:not(.quick-notes)');
  const n = await groups.count();
  for (let i = 0; i < n; i++) {
    const first = groups.nth(i).locator('button:not([disabled])').first();
    if (await first.count()) await first.click();
  }
}
async function addItem(page, card) {
  await clearOverlays(page);
  await card.click();
  await pause(page, 500);
  if (await page.locator('.overlay .sheet').count()) {
    await satisfyPicker(page);
    await page.locator('[data-testid=options-confirm]').click();
    await page.locator('.overlay').waitFor({ state: 'detached', timeout: 5000 }).catch(() => {});
  }
  await pause(page, 400);
}
const itemCard = (page, name) => page.locator('.item-wrap', { has: page.locator(`text="${name}"`) }).locator('.item-card:not([disabled])').first();
async function addByName(page, name) {
  await page.locator('#menu-search').fill(name);
  await pause(page, 500);
  await addItem(page, itemCard(page, name));
  await page.locator('.search-clear').click().catch(() => {});
  await pause(page, 300);
}
async function setTheme(page, mode) {
  const toggle = page.locator('.topbar .theme-toggle');
  for (let i = 0; i < 3 && !(await toggle.getAttribute('title')).includes(mode); i++) {
    await toggle.click();
    await pause(page, 200);
  }
}
// Product bug seen on 2026-10-19: ClientHostExtensions.ImageUrl treats "/images/menu/x.jpg" as an absolute URI (a Unix file
// path) in the browser build, so every picture gets src="file:///images/menu/x.jpg" and is blocked. repairImages only
// rewrites the DOM so the demo can still show what the pictures look like; it is always a separate, labelled step.
const loadedThumbs = (page, sel) => page.locator(sel).evaluateAll(els => els.filter(e => e.complete && e.naturalWidth > 0).length);
const thumbSrc = (page, sel) => page.locator(sel).first().evaluate(e => e.getAttribute('src') ?? e.src);
const repairImages = page => page.evaluate(origin => {
  for (const img of document.querySelectorAll('img')) {
    const s = img.getAttribute('src') ?? '';
    if (s.startsWith('file:///')) { img.style.display = ''; img.src = origin + s.slice('file://'.length); }
  }
}, API);
async function pickChannel(page, testid) {
  await navSheet(page, 'New order');
  await page.locator(`[data-testid=${testid}]`).click();
  await page.locator('#menu-search').waitFor();
  await pause(page, 700);
}

// ---------------- set-up through the API ----------------
const prep = [];
const waiterToken = await apiLogin('waiter1', 'Pass@123');
const adminToken = await apiLogin('admin', ADMIN_PASSWORD);
const mapRes = await apiGet(waiterToken, '/api/tables');
const allTables = mapRes.data.sections.filter(s => s.isActive).flatMap(s => s.tables.filter(t => t.isActive).map(t => ({ ...t, section: s.name })));
// tables another session is driving at the same time (the desktop client uses T05) are never picked as a fallback
const RESERVED = (process.env.EXCLUDE_TABLES ?? 'T05').split(',').map(s => s.trim());
const free = allTables.filter(t => t.status === 'Available' && !RESERVED.includes(t.code));
let mainT = allTables.find(t => t.code === TABLE);
if (!mainT || mainT.status !== 'Available') {
  const pick = free[0];
  prep.push(`Requested table ${TABLE} is ${mainT?.status ?? 'missing'} (it has an open order of an earlier run); the main table is ${pick?.code} (${pick?.section}) instead.`);
  mainT = pick;
}
TABLE = mainT.code;
const spareT = allTables.find(t => t.code === SPARE && t.status === 'Available' && t.code !== TABLE) ?? free.find(t => t.code !== TABLE);
if (spareT?.code !== SPARE) prep.push(`Spare table ${SPARE} is not free; the offline drill uses ${spareT?.code ?? '(none)'}.`);
SPARE = spareT?.code;
log(`main table ${TABLE}, spare ${SPARE}`);

// reservation for the main table starting in ~30 min (party of 2), unless one is already coming up
if (!mainT.upcomingReservation) {
  const start = new Date(Date.now() + 30 * 60000);
  const end = new Date(start.getTime() + 90 * 60000);
  const r = await apiPost(waiterToken, '/api/reservations', {
    tableId: mainT.id, customerName: 'Meera Nair', customerPhone: '9876501234', partySize: 2,
    startTimeUtc: start.toISOString(), endTimeUtc: end.toISOString(), notes: 'Anniversary dinner, window side if possible',
  });
  prep.push(r.success ? `Reservation #${r.data.id} created for ${TABLE} (Meera Nair, party of 2, starts ${start.toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit' })}).` : `Reservation was not created: ${r.message}`);
} else {
  prep.push(`${TABLE} already had an upcoming reservation (${mainT.upcomingReservation.customerName}); kept.`);
}

// device clean-up: re-activate this profile's terminals if an earlier run deactivated them
const guidRe = /WEB-(?:WAITER|KITCHEN)-[0-9A-F]+/gi; // the device identity kept in the browser state is the terminal name
const myDeviceIds = () => {
  const ids = new Set();
  for (const role of ['waiter', 'kitchen']) if (fs.existsSync(stateFile(role))) for (const m of fs.readFileSync(stateFile(role), 'utf8').matchAll(guidRe)) ids.add(m[0].toUpperCase());
  return ids;
};
{
  const devs = (await apiGet(adminToken, '/api/admin/devices')).data ?? [];
  for (const d of devs) if (myDeviceIds().has(d.name.toUpperCase()) && !d.isActive) {
    const r = await apiPost(adminToken, `/api/admin/devices/${d.id}/activate`);
    prep.push(`Re-activated terminal ${d.name} (${r.success ? 'ok' : r.message}).`);
  }
}

const browser = await chromium.launch({ channel: CHANNEL, headless: true });

// demo pictures: the three items the flow orders get a generated plate picture when they have none (menu image upload
// route: POST /api/menu-items/{id}/image, multipart). The kitchen ticket shows the thumbnails.
const PICTURE_ITEMS = [['Chicken Biryani', 38, '#d98a2b'], ['Butter Naan', 42, '#e8c27a'], ['Masala Chai', 25, '#a8693a'], ['Gulab Jamun', 15, '#7a2e1d']];
{
  const menu = (await apiGet(waiterToken, '/api/menu')).data;
  const pg = await browser.newPage({ viewport: { width: 480, height: 480 } });
  await pg.setContent('<canvas id="c" width="480" height="480"></canvas>');
  for (const [name, hue, food] of PICTURE_ITEMS) {
    const it = menu.items.find(i => i.name === name);
    if (!it || it.imageUrl) continue;
    const b64 = await pg.evaluate(([label, h, col]) => {
      const c = document.getElementById('c').getContext('2d');
      const g = c.createLinearGradient(0, 0, 480, 480);
      g.addColorStop(0, `hsl(${h},55%,78%)`); g.addColorStop(1, `hsl(${h + 20},45%,58%)`);
      c.fillStyle = g; c.fillRect(0, 0, 480, 480);
      c.shadowColor = 'rgba(0,0,0,.35)'; c.shadowBlur = 24; c.shadowOffsetY = 10;
      c.fillStyle = '#fafafa'; c.beginPath(); c.arc(240, 230, 180, 0, 7); c.fill();
      c.shadowColor = 'transparent'; c.fillStyle = '#ececec'; c.beginPath(); c.arc(240, 230, 140, 0, 7); c.fill();
      c.fillStyle = col; c.beginPath(); c.arc(240, 230, 100, 0, 7); c.fill();
      for (let i = 0; i < 26; i++) { c.fillStyle = `hsla(${h + (i * 7) % 40},60%,${35 + (i * 11) % 30}%,.85)`; c.beginPath(); c.arc(240 + Math.cos(i * 2.4) * (20 + (i * 13) % 70), 230 + Math.sin(i * 2.4) * (20 + (i * 9) % 70), 14 + (i % 4) * 4, 0, 7); c.fill(); }
      c.fillStyle = 'rgba(0,0,0,.55)'; c.font = 'bold 34px sans-serif'; c.textAlign = 'center'; c.fillText(label, 240, 450);
      return document.getElementById('c').toDataURL('image/jpeg', 0.85).split(',')[1];
    }, [name, hue, food]);
    const form = new FormData();
    form.append('file', new Blob([Buffer.from(b64, 'base64')], { type: 'image/jpeg' }), name.toLowerCase().replace(/\W+/g, '-') + '.jpg');
    const r = await (await fetch(`${API}/api/menu-items/${it.id}/image`, { method: 'POST', headers: { Authorization: 'Bearer ' + adminToken }, body: form })).json();
    prep.push(`Demo picture ${r.success ? 'uploaded' : 'FAILED (' + r.message + ')'} for menu item ${name} (#${it.id}).`);
  }
  await pg.close();
}

const common = { serviceWorkers: 'block', deviceScaleFactor: 2, hasTouch: true, colorScheme: 'light', locale: 'en-IN', timezoneId: 'Asia/Kolkata' };
const waiterCtx = await browser.newContext({ ...common, viewport: { width: 390, height: 844 }, isMobile: true, storageState: stateFor('waiter'),
  ...(RECORD ? { recordVideo: { dir: path.join(RECORD, 'waiter'), size: { width: 780, height: 1688 } } } : {}) });
const kitchenCtx = await browser.newContext({ ...common, viewport: { width: 1024, height: 768 }, storageState: stateFor('kitchen'),
  ...(RECORD ? { recordVideo: { dir: path.join(RECORD, 'kitchen'), size: { width: 1024, height: 768 } } } : {}) });
if (RECORD) { await waiterCtx.addInitScript(POINTER); await kitchenCtx.addInitScript(POINTER); }
// (web-flow.mjs rewrote /app/_content/ to /_content/ for builds before 20.11; on current builds that breaks the CSS, so it is gone.)
const hubSockets = [];
await waiterCtx.routeWebSocket(/\/hub/, ws => { ws.connectToServer(); hubSockets.push(ws); });
const w = await waiterCtx.newPage();
videoT0.waiter = Date.now();
const k = await kitchenCtx.newPage();
videoT0.kitchen = Date.now();
for (const [role, p] of [['waiter', w], ['kitchen', k]]) {
  p.setDefaultTimeout(15000);
  p.on('console', m => { if (m.type() === 'error') consoleErrors.push({ role, url: p.url(), text: m.text().slice(0, 400) }); });
  p.on('pageerror', e => consoleErrors.push({ role, url: p.url(), text: 'pageerror: ' + e.message.slice(0, 400) }));
}

let taLabel = null;
let dineOrderId = null;
const issues = [];
const W = (chapter, name, act, exp, shotName, fn, o) => step(chapter, name, act, exp, w, WF, shotName, fn, o);
const K = (chapter, name, act, exp, shotName, fn, o) => step(chapter, name, act, exp, k, KF, shotName, fn, o);

try {
  // ================= 02 WAITER (phone) =================
  await W('Waiter: sign in', 'Sign-in screen', `Open ${BASE}signin, pick Waiter, type waiter1 / password`, 'Sign-in card with the Waiter role selected and the username filled', 'sign-in', async () => {
    await signIn(w, 'Waiter', 'waiter1');
    if ((await w.locator('input[autocomplete=username]').inputValue()) !== 'waiter1') throw new Error('username not filled');
  });
  await W('Waiter: sign in', 'Home with KPIs', 'Tap Sign in', 'Home opens with the four KPI tiles and the quick actions', 'home-kpis', async () => {
    await w.locator('button[type=submit]').click();
    await w.waitForURL(/\/home|\/tables/);
    await bold(w, '[data-testid=home-kpis]');
    await bold(w, '[data-testid=quick-actions]');
  }, { wait: 1500 });

  await W('Waiter: tables', 'Tables in List mode', 'Bottom nav Tables, tap List in the List/Map toggle', 'List/Map toggle visible, table cards in a grid', 'tables-list', async () => {
    await nav(w, 'Tables');
    await w.locator('.table-card, [data-testid=floor-tile]').first().waitFor();
    await bold(w, '.tables-toolbar .segmented');
    await w.locator('.tables-toolbar .segment', { hasText: 'List' }).click();
    await w.locator('.table-card').first().waitFor();
    if ((await w.locator('.tables-toolbar .segment.on').first().innerText()).trim() !== 'List') throw new Error('List not selected');
  }, { wait: 1200 });
  await W('Waiter: tables', 'List shows the reservation badge', `Filter the list to ${mainT.section}`, `${TABLE} card carries the reservation badge`, 'tables-list-reservation-badge', async () => {
    await w.locator('[data-testid=section-chips] .filter-chip', { hasText: mainT.section }).click();
    await pause(w, 500);
    const card = w.locator('.table-card', { has: w.locator('.table-code', { hasText: new RegExp(`^${TABLE}$`) }) });
    await card.first().waitFor();
    await card.locator('[data-testid=table-reservation-badge]').waitFor({ timeout: 5000 });
  });
  await W('Waiter: tables', 'Tables in Map mode (Ground Floor plan)', 'Choose All sections, tap Map, pick Ground Floor', 'The Ground Floor floor plan renders with table tiles', 'tables-map-ground-floor', async () => {
    await w.locator('.tables-toolbar .segment', { hasText: 'Map' }).click();
    await w.locator('[data-testid=section-chips] .filter-chip', { hasText: 'Ground Floor' }).click();
    await bold(w, '[data-testid=floor-map]');
    const tiles = await w.locator('[data-testid=floor-tile]').count();
    if (tiles < 3) throw new Error(`only ${tiles} tiles on the plan`);
    return `${tiles} tiles on the plan`;
  }, { wait: 1200 });
  await W('Waiter: tables', 'Map mode: section without a plan', `Choose ${mainT.section} in Map mode`, `The section is shown (plan if it has one, else the grid with a "No floor plan" hint) and the ${TABLE} badge is visible`, 'tables-map-other-section', async () => {
    await w.locator('[data-testid=section-chips] .filter-chip', { hasText: mainT.section }).click();
    await pause(w, 800);
    const hint = await w.locator('[data-testid=map-hint]').count();
    const badge = await w.locator('[data-testid=table-reservation-badge]').count();
    if (!badge) throw new Error('reservation badge not visible in map mode');
    return hint ? 'section has no plan: grid with hint' : 'section has a plan';
  });
  await W('Waiter: tables', 'Back to All, List mode', 'Select All, tap List', 'The list is back for the order flow', null, async () => {
    await w.locator('[data-testid=section-chips] .filter-chip', { hasText: 'All' }).first().click();
    await w.locator('.tables-toolbar .segment', { hasText: 'List' }).click();
    await w.locator('.table-card').first().waitFor();
  });

  await W('Waiter: table & reservation', `Table details ${TABLE} with the reservation`, `Open ${TABLE} from the list`, 'Table details with the reservation banner (guest, party of 2, time window) and a Seat button', 'table-details-reservation', async () => {
    await openTable(w, TABLE);
    await bold(w, '[data-testid=table-reservation-banner]');
    if (!(await w.locator('[data-testid=table-reservation-banner]').innerText()).includes('Meera')) throw new Error('banner is not the demo reservation');
  });
  await W('Waiter: table & reservation', 'Seat the reservation', 'Tap Seat on the reservation banner', 'The table becomes occupied and "New order" is offered', 'table-seated', async () => {
    let note = 'seated through the reservation Seat button';
    if (await w.locator('[data-action=seat-reservation]').count()) {
      await w.locator('[data-action=seat-reservation]').click();
      await pause(w, 1500);
    }
    if (!(await w.locator('.action-list [data-action=new-order]').count())) {
      // fall back to the guests stepper + Seat guests
      note = 'Seat button did not occupy the table; used the guests stepper';
      if (await w.locator('[data-action=occupy]').count()) {
        const value = async () => parseInt(await w.locator('.stepper-value').first().innerText(), 10);
        while ((await value()) < 2) await w.locator('.stepper button[aria-label$="one more"]').first().click();
        await w.locator('[data-action=occupy]').click();
        await pause(w, 1200);
      }
    }
    await w.locator('.action-list [data-action=new-order]').waitFor({ timeout: 8000 });
    return note;
  });

  await W('Waiter: dine-in order', 'New order screen with the order type chips', 'Tap New order on the table', 'Order builder with the Dine-in / Takeaway / Delivery chips, Dine-in selected for the table', 'order-type-chips', async () => {
    await action(w, 'new-order');
    await w.locator('#menu-search').waitFor();
    await bold(w, '[data-testid=channel-chips]');
    const chips = await w.locator('[data-testid=channel-chips] .filter-chip').allInnerTexts();
    if (chips.length < 3) throw new Error('chips: ' + chips.join('|'));
    if (!(await w.locator('[data-testid=channel-chips] .filter-chip.on').first().innerText()).includes('Dine')) throw new Error('Dine-in not selected');
    if (await w.locator('.banner.info', { hasText: 'restored' }).count()) {
      await w.locator('[data-testid=cart-review]').click();
      for (let i = 0; i < 40 && await w.locator('.cart-line').count(); i++) { await w.locator('.cart-line [data-testid=cart-line-delete]').first().click(); await pause(w, 150); }
      await closeSheet(w);
      return 'a restored draft was cleared first';
    }
  }, { wait: 1200 });
  await W('Waiter: dine-in order', 'Search the menu', 'Type "biry" in the search box', 'Biryani results across categories', 'order-search', async () => {
    await w.locator('#menu-search').fill('biry');
    await pause(w, 600);
    if ((await w.locator('.item-wrap').count()) < 2) throw new Error('fewer than 2 results');
  });
  await W('Waiter: dine-in order', 'Favourites', 'Star Chicken Biryani and Mutton Biryani, clear the search, open the Favourites chip', 'Both items appear under Favourites', 'favourites', async () => {
    for (const n of ['Chicken Biryani', 'Mutton Biryani']) {
      const star = w.locator('.item-wrap', { has: w.locator(`text="${n}"`) }).locator('.item-star').first();
      if ((await star.getAttribute('aria-pressed')) !== 'true') await star.click();
      await pause(w, 200);
    }
    await w.locator('.search-clear').click();
    await pause(w, 400);
    await w.locator('#chip-favourites').click();
    await pause(w, 400);
    const names = await w.locator('.item-wrap').allInnerTexts();
    if (!names.some(t => t.includes('Chicken Biryani'))) throw new Error('Chicken Biryani not under Favourites');
  });
  await W('Waiter: dine-in order', 'Item options sheet with a note', 'Tap Chicken Biryani, pick the options, a quick note and type a note', 'Options sheet with option groups, quick-note chips, the note text and the quantity', 'item-options-note', async () => {
    await itemCard(w, 'Chicken Biryani').click();
    await w.locator('.overlay .sheet').waitFor();
    await satisfyPicker(w);
    await w.locator('.quick-notes button', { hasText: 'Less oil' }).click().catch(() => {});
    await w.locator('[data-testid=item-note]').fill('Less oil, extra raita on the side');
    if (!(await w.locator('[data-testid=item-note]').inputValue()).includes('raita')) throw new Error('note not typed');
  });
  await W('Waiter: dine-in order', 'Cart bar after adding three items', 'Confirm the biryani, add Butter Naan (Breads) and Masala Chai (Beverages)', 'Cart bar shows 3 lines', 'cart', async () => {
    await w.locator('[data-testid=options-confirm]').click();
    await w.locator('.overlay').waitFor({ state: 'detached', timeout: 5000 }).catch(() => {});
    await pause(w, 400);
    await w.locator('.chip-btn', { hasText: 'Breads' }).click();
    await pause(w, 400);
    await addItem(w, itemCard(w, 'Butter Naan'));
    await w.locator('.chip-btn', { hasText: 'Beverages' }).click();
    await pause(w, 400);
    await addItem(w, itemCard(w, 'Masala Chai'));
    await bold(w, '.cart-bar');
    await w.locator('.cart-bar').scrollIntoViewIfNeeded();
  });
  await W('Waiter: dine-in order', 'Review sheet', 'Tap Review order, raise the naan quantity, type order notes', 'Review sheet lists the lines with controls, guests, order notes and the Send button', 'review-sheet', async () => {
    await clearOverlays(w);
    await w.locator('[data-testid=cart-review]').click();
    await w.locator('.overlay .sheet .cart-line').first().waitFor();
    await w.locator('.cart-line').nth(1).locator('button[aria-label="One more"]').click();
    await w.locator('[data-testid=order-notes]').fill('Anniversary table: bring the dessert last');
    await w.locator('[data-testid=order-notes]').blur();
    await pause(w, 300);
    await bold(w, '[data-testid=send]');
  });
  await W('Waiter: dine-in order', 'Send to kitchen', 'Tap Send to kitchen', 'Back on the table details with the order submitted and a success toast', 'sent-to-kitchen', async () => {
    await w.locator('[data-testid=send]').click();
    await w.waitForURL(/\/tables\/\d+$/, { timeout: 10000 });
    dineOrderId = null;
    await w.locator('.detail-card h1').first().waitFor();
    await bold(w, '[data-testid=order-details]');
  }, { wait: 500 });
  await W('Waiter: order details', 'Order details: Kitchen tickets and Print KOT', 'Scroll to the Kitchen tickets list', 'One ticket row per station, each with a Print KOT button', 'order-kitchen-tickets', async () => {
    await bold(w, '[data-testid=order-tickets]');
    await w.locator('[data-testid=print-kot]').first().waitFor();
    await w.locator('[data-testid=order-tickets]').scrollIntoViewIfNeeded();
    return `${await w.locator('[data-testid=order-ticket]').count()} ticket(s)`;
  });
  let kotPopup = null;
  await W('Waiter: order details', 'Print KOT: button pressed', 'Tap Print KOT on the first ticket', 'A new tab opens with the ticket PDF', 'print-kot-pressed', async () => {
    await clearOverlays(w);
    [kotPopup] = await Promise.all([
      waiterCtx.waitForEvent('page', { timeout: 15000 }),
      w.locator('[data-testid=print-kot]').first().click(),
    ]);
  }, { wait: 300 });
  await step('Waiter: order details', 'Print KOT: the PDF tab', 'Look at the new tab', 'The KOT PDF is shown (blob: PDF url)', kotPopup ?? w, WF, 'print-kot-pdf-tab', async () => {
    if (!kotPopup) throw new Error('no popup opened');
    await kotPopup.waitForLoadState('domcontentloaded').catch(() => {});
    await pause(kotPopup, 2500);
    const url = kotPopup.url();
    if (!/^blob:|\.pdf/i.test(url)) throw new Error('popup url is ' + url.slice(0, 60));
    return 'popup url ' + url.slice(0, 40) + '…';
  }, { measure: false, wait: 500 });
  if (kotPopup) await kotPopup.close().catch(() => {});
  await W('Waiter: order details', 'Items tab with the note', 'Tap the Items tab', 'Lines with options, the note and the kitchen status', 'order-items', async () => {
    await w.bringToFront();
    await w.locator('.tabs .tab', { hasText: 'Items' }).click();
    await bold(w, '[data-testid=order-items]');
  });

  // ---- takeaway ----
  await W('Waiter: takeaway order', 'Takeaway order builder (no table)', 'New order tab, pick Takeaway under "Channels (no table)"', 'Order builder titled Takeaway with the Takeaway chip selected', 'takeaway-builder', async () => {
    await pickChannel(w, 'picker-takeaway');
    if (await w.locator('.banner.info', { hasText: 'restored' }).count()) {
      await w.locator('[data-testid=cart-review]').click();
      for (let i = 0; i < 40 && await w.locator('.cart-line').count(); i++) { await w.locator('.cart-line [data-testid=cart-line-delete]').first().click(); await pause(w, 150); }
      await closeSheet(w);
    }
    if (!(await w.locator('[data-testid=channel-chips] .filter-chip.on').first().innerText()).includes('Takeaway')) throw new Error('Takeaway chip not selected');
  });
  await W('Waiter: takeaway order', 'Send the takeaway order', 'Add Gulab Jamun and Butter Naan, review, Send to kitchen', 'Sent: My orders opens with the takeaway TA-nnnn card', 'takeaway-sent', async () => {
    await addByName(w, 'Gulab Jamun');
    await addByName(w, 'Butter Naan');
    await clearOverlays(w);
    await w.locator('[data-testid=cart-review]').click();
    await w.locator('.overlay .sheet .cart-line').first().waitFor();
    await w.locator('[data-testid=send]').click();
    await w.waitForURL(/\/my-orders/, { timeout: 10000 });
    const card = w.locator('[data-testid=order-card]', { hasText: /TA-\d+/ }).first();
    await card.waitFor();
    taLabel = (await card.innerText()).match(/TA-\d+/)[0];
    return 'label ' + taLabel;
  }, { wait: 1200 });

  // ---- held draft ----
  await W('Waiter: held order', 'Save a draft from a new order', 'New order, Takeaway, add Masala Chai, Review, Save draft', 'A confirmation toast and My orders opens', 'draft-saved', async () => {
    await pickChannel(w, 'picker-takeaway');
    if (await w.locator('.banner.info', { hasText: 'restored' }).count()) {
      await w.locator('[data-testid=cart-review]').click();
      for (let i = 0; i < 40 && await w.locator('.cart-line').count(); i++) { await w.locator('.cart-line [data-testid=cart-line-delete]').first().click(); await pause(w, 150); }
      await closeSheet(w);
    }
    await addByName(w, 'Masala Chai');
    await clearOverlays(w);
    await w.locator('[data-testid=cart-review]').click();
    await w.locator('.overlay .sheet .cart-line').first().waitFor();
    await w.locator('[data-testid=save-draft]').click();
    await w.waitForURL(/\/my-orders/, { timeout: 10000 });
    const toast = (await w.locator('.toasts .toast').first().innerText({ timeout: 3000 }).catch(() => '')).trim();
    return `toast: "${toast}"`;
  }, { wait: 300 });
  await W('Waiter: held order', 'Held tab lists the saved draft', 'Open the Held tab', 'The draft just saved is listed with a Resume button', 'held-tab-empty', async () => {
    await clearOverlays(w);
    await w.locator('.tabs .tab', { hasText: 'Held' }).click();
    await w.locator('[data-testid=held-order-card]').first().waitFor({ timeout: 5000 }).catch(() => { throw new Error('Held tab is empty: "Save draft" on a new order only keeps the draft on the phone (local), the Held tab lists server-side drafts'); });
  }, { wait: 800 });
  await W('Waiter: held order', 'Held tab with a server-side draft (set-up through the API)', 'POST /api/orders (Submit=false) as waiter1, reload My orders, open Held', 'The held takeaway order with a Resume button', 'held-tab', async () => {
    const r = await apiPost(waiterToken, '/api/orders', { orderType: 'Takeaway', guestCount: 1, notes: 'Held for the customer on the phone', submit: false,
      items: [{ menuItemId: 13, quantity: 1, notes: 'Less sugar' }, { menuItemId: 17, quantity: 2 }] });
    if (!r.success) throw new Error('API draft failed: ' + r.message);
    await w.reload({ waitUntil: 'domcontentloaded' });
    await w.locator('[data-testid=order-card]').first().waitFor();
    await w.locator('.tabs .tab', { hasText: 'Held' }).click();
    await bold(w, '[data-testid=held-order-card]');
    await bold(w, '[data-testid=resume-order]');
    return `API draft order #${r.data.orderNumber}`;
  }, { wait: 1000 });
  await W('Waiter: held order', 'Resume the held order', 'Tap Resume', 'The order builder opens as "Edit draft" with the held item in the cart', 'held-resumed', async () => {
    await w.locator('[data-testid=resume-order]').first().click();
    await w.locator('#menu-search').waitFor();
    await bold(w, '.cart-bar');
  }, { wait: 1000 });
  await W('Waiter: held order', 'Send the resumed order', 'Review, Send to kitchen', 'Sent; My orders shows the order as active', 'held-resumed-sent', async () => {
    await clearOverlays(w);
    await w.locator('[data-testid=cart-review]').click();
    await w.locator('.overlay .sheet .cart-line').first().waitFor();
    await w.locator('[data-testid=send]').click();
    await w.waitForURL(/\/my-orders/, { timeout: 10000 });
    await w.locator('[data-testid=order-card]').first().waitFor();
  }, { wait: 1000 });

  await W('Waiter: orders', 'My orders (Active)', 'Open the Active tab', 'The dine-in order and both takeaway orders (TA-nnnn) are listed', 'my-orders', async () => {
    await w.locator('.tabs .tab', { hasText: 'Active' }).click();
    await pause(w, 600);
    const n = await w.locator('[data-testid=order-card]').count();
    if (n < 3) throw new Error(`only ${n} cards`);
    if ((await w.locator('[data-testid=order-card]', { hasText: /TA-\d+/ }).count()) < 2) throw new Error('fewer than 2 takeaway cards');
  }, { wait: 1200 });
  await W('Waiter: orders', 'Takeaway order details', `Open the ${taLabel ?? 'TA'} card`, 'Details page for an order with no table: the TA label, tickets and Print KOT', 'takeaway-details', async () => {
    await w.locator('[data-testid=order-card]', { hasText: new RegExp(taLabel ?? 'TA-\\d+') }).first().click();
    await w.waitForURL(/\/orders\/\d+$/, { timeout: 5000 }).catch(() => {});
    await w.locator('[data-testid=order-details]').first().waitFor({ timeout: 6000 }).catch(async () => {
      throw new Error(`details page did not open (url ${w.url().replace(BASE, '/app/')}): "${(await w.locator('.empty, .banner').first().innerText().catch(() => '')).trim().slice(0, 60)}". TableDetailsPage redirects an order without a table to "waiter/orders", a route that does not exist`);
    });
    const txt = await w.locator('body').innerText();
    if (!/TA-\d+/.test(txt)) throw new Error('no TA label on the details page');
    await w.locator('[data-testid=order-tickets]').scrollIntoViewIfNeeded().catch(() => {});
  }, { wait: 1200 });

  await W('Waiter: bell, settings, theme', 'Bell panel', 'Tap the bell in the top bar', 'Notifications panel opens', 'bell-panel', async () => {
    await clearOverlays(w);
    await w.locator('.topbar button.bell').click();
    await bold(w, 'button[aria-label="Close notifications"]');
  });
  await W('Waiter: bell, settings, theme', 'Settings', 'Close the panel, More > Settings', 'Settings page', 'settings', async () => {
    await w.locator('button[aria-label="Close notifications"]').click();
    await navSheet(w, 'More');
    await w.locator('.sheet a.more-link', { hasText: 'Settings' }).click();
    await w.waitForURL(/\/settings/);
  }, { wait: 1000 });
  await W('Waiter: bell, settings, theme', 'Dark theme', 'Tables page, theme toggle to dark', 'Tables page rendered in the dark theme', 'tables-dark', async () => {
    await nav(w, 'Tables');
    await w.locator('.table-card, [data-testid=floor-tile]').first().waitFor();
    await setTheme(w, 'dark');
    await pause(w, 400);
    const bg = await w.evaluate(() => getComputedStyle(document.body).backgroundColor);
    const m = bg.match(/\d+/g).map(Number);
    if (m[0] + m[1] + m[2] > 300) throw new Error('background is not dark: ' + bg);
    await setTheme(w, 'system');
    await setTheme(w, 'dark');
    return 'body background ' + bg;
  }, { wait: 1000 });
  await setTheme(w, 'system');

  // ================= 03 KITCHEN (tablet) =================
  await K('Kitchen: sign in', 'Sign-in screen', 'Open the sign-in, pick Kitchen, type kitchen1 / password', 'Sign-in card with the Kitchen role selected', 'sign-in', async () => {
    await signIn(k, 'Kitchen', 'kitchen1');
    if ((await k.locator('input[autocomplete=username]').inputValue()) !== 'kitchen1') throw new Error('username not filled');
  });
  let stationFirst = false;
  const tTable = k.locator('article.ticket', { has: k.locator('.ticket-table', { hasText: new RegExp('^' + TABLE + '$') }) });
  const tTake = k.locator('article.ticket', { has: k.locator('.ticket-table', { hasText: /^TA-\d+$/ }) });
  await K('Kitchen: board', 'Board with KPI strip and the new tickets', 'Tap Sign in', `The board shows the New/Preparing/Ready KPI strip, the ${TABLE} dine-in ticket and the takeaway ticket(s) with the Counter chip`, 'board-new-tickets', async () => {
    await k.locator('button[type=submit]').click();
    await k.waitForURL(/\/kitchen/);
    await pause(k, 1500);
    if (k.url().includes('/kitchen/station')) {
      stationFirst = true;
      await k.locator('.card button', { hasText: 'All stations' }).click();
      await k.waitForURL(/\/kitchen$/);
    }
    await tTable.first().waitFor();
    await bold(k, '#kds-kpis');
    await tTake.first().waitFor({ timeout: 8000 });
    const counter = await tTake.first().locator('.ticket-channel', { hasText: 'Counter' }).count();
    if (!counter) throw new Error('takeaway ticket has no Counter chip');
    return `${await tTable.count()} ticket(s) for ${TABLE}, ${await tTake.count()} takeaway ticket(s)` + (stationFirst ? '; first sign-in landed on the station chooser' : '');
  }, { wait: 1200 });
  await K('Kitchen: board', `The ${TABLE} and takeaway tickets in the New column`, 'Scroll the board to the new tickets (the board also holds older tickets of the dev DB)', 'Both new tickets are in view', 'board-new-tickets-scrolled', async () => {
    await tTable.first().scrollIntoViewIfNeeded();
    await tTake.last().scrollIntoViewIfNeeded();
    await tTable.first().scrollIntoViewIfNeeded();
  }, { wait: 800 });
  await K('Kitchen: board', 'Ticket detail pane with item pictures', `Tap the ${TABLE} ticket head`, 'Right pane with guests, order notes, lines with pictures, Print KOT and the progress timeline', 'ticket-detail-pictures', async () => {
    await tTable.first().locator('.ticket-top').click();
    await bold(k, '[data-testid=ticket-detail]');
    await k.locator('[data-testid=guests]').waitFor({ timeout: 5000 }).catch(() => {});
    await k.locator('.kds-detail .ticket-thumb').first().waitFor({ state: 'attached', timeout: 6000 });
    await pause(k, 800);
    const shown = await loadedThumbs(k, '.kds-detail .ticket-thumb');
    if (!shown) throw new Error('thumbnails are in the page but none loaded: ' + await thumbSrc(k, '.kds-detail .ticket-thumb'));
    return `${shown} picture(s) loaded in the pane`;
  }, { wait: 1200 });
  await K('Kitchen: board', 'Ticket detail pane with item pictures (DOM workaround)', 'Same pane; the script rewrites the broken file:/// image URLs to the server origin (simulates the fix of the URL bug)', 'The item pictures render in the pane', 'ticket-detail-pictures-workaround', async () => {
    await repairImages(k);
    await pause(k, 1000);
    const shown = await loadedThumbs(k, '.kds-detail .ticket-thumb');
    if (!shown) throw new Error('still no picture after the URL repair');
    return `${shown} picture(s) after the repair (NOT what the shipped build shows)`;
  }, { wait: 500 });
  await K('Kitchen: board', 'Takeaway ticket detail', 'Close the pane, open the takeaway ticket', 'Detail pane with the Counter / TA channel badge', 'ticket-detail-takeaway', async () => {
    await k.locator('.detail-pane button[aria-label="Close"]').click();
    await k.locator('.detail-overlay').waitFor({ state: 'detached' });
    await tTake.first().locator('.ticket-top').click();
    await bold(k, '[data-testid=ticket-detail]');
    await bold(k, '[data-testid=channel-badge]');
  }, { wait: 1000 });
  await K('Kitchen: station', 'Station page: Show item pictures on', 'Close the pane, Station in the top bar, tick "Show item pictures on tickets"', 'The checkbox is ticked', 'station-show-pictures', async () => {
    await k.locator('.detail-pane button[aria-label="Close"]').click();
    await k.locator('.detail-overlay').waitFor({ state: 'detached' });
    await kGo(k, 'Station');
    const cb = k.locator('[data-testid=show-pictures]');
    await cb.waitFor();
    if (!(await cb.isChecked())) await cb.check();
    await pause(k, 600);
    if (!(await cb.isChecked())) throw new Error('checkbox did not stay ticked');
  });
  await K('Kitchen: board', 'Board with thumbnails on the cards', 'Back to the Kitchen board', 'Ticket cards show the item pictures', 'board-thumbnails', async () => {
    await kGo(k, 'Kitchen');
    await tTable.first().waitFor();
    await k.locator('article.ticket .ticket-thumb').first().waitFor({ state: 'attached', timeout: 6000 });
    await pause(k, 800);
    const shown = await loadedThumbs(k, 'article.ticket .ticket-thumb');
    if (!shown) throw new Error('thumbnails are on the cards but none loaded: ' + await thumbSrc(k, 'article.ticket .ticket-thumb'));
    return `${shown} thumbnail(s) loaded on the board`;
  }, { wait: 1500 });
  await K('Kitchen: board', 'Board thumbnails (DOM workaround)', 'Same board; broken file:/// image URLs rewritten to the server origin', 'The cards show the item thumbnails', 'board-thumbnails-workaround', async () => {
    await repairImages(k);
    await pause(k, 1000);
    const shown = await loadedThumbs(k, 'article.ticket .ticket-thumb');
    if (!shown) throw new Error('still no thumbnail after the URL repair');
    return `${shown} thumbnail(s) after the repair (NOT what the shipped build shows)`;
  }, { wait: 500 });
  async function pressAll(act) {
    for (let i = 0; i < 10; i++) {
      const btn = tTable.locator(`button[data-action="${act}"]`).first();
      if (!(await btn.count())) return i;
      await clearOverlays(k);
      await btn.click();
      await pause(k, 1200);
    }
    return 10;
  }
  await K('Kitchen: cooking', `Accept the ${TABLE} ticket(s)`, 'Press Accept on every ticket of the table', 'Tickets show the Accepted chip and a Start button', 'accepted', async () => {
    const n = await pressAll('accept');
    if (!n) throw new Error('no Accept button found');
    await tTable.first().locator('button[data-action="start"]').waitFor({ timeout: 6000 });
    return `${n} ticket(s)`;
  });
  await K('Kitchen: cooking', 'Start preparing', 'Press Start', 'Tickets move to Preparing with a Ready button', 'preparing', async () => {
    const n = await pressAll('start');
    if (!n) throw new Error('no Start button found');
    await k.evaluate(() => scrollTo(0, 0));
    await tTable.first().locator('button[data-action="ready"]').waitFor({ timeout: 6000 });
  });
  await K('Kitchen: cooking', 'Mark ready', 'Press Ready', 'Tickets move to the Ready column', 'ready', async () => {
    const n = await pressAll('ready');
    if (!n) throw new Error('no Ready button found');
    await k.evaluate(() => scrollTo(0, 0));
    await pause(k, 800);
  });
  await K('Kitchen: completed, bell, theme', 'Completed list', 'Top bar > Completed', 'Completed tickets page', 'completed', async () => {
    await kGo(k, 'Completed');
  }, { wait: 1200 });
  await K('Kitchen: completed, bell, theme', 'Bell panel', 'Back to the board, tap the bell', 'Notifications panel opens', 'bell-panel', async () => {
    await kGo(k, 'Kitchen');
    await clearOverlays(k);
    await k.locator('.topbar button.bell').click();
    await bold(k, 'button[aria-label="Close notifications"]');
  });
  await K('Kitchen: completed, bell, theme', 'Dark theme', 'Close the panel, theme toggle to dark', 'Board in the dark theme', 'board-dark', async () => {
    await k.locator('button[aria-label="Close notifications"]').click();
    await setTheme(k, 'dark');
    await pause(k, 400);
    const bg = await k.evaluate(() => getComputedStyle(document.body).backgroundColor);
    const m = bg.match(/\d+/g).map(Number);
    if (m[0] + m[1] + m[2] > 300) throw new Error('background is not dark: ' + bg);
  }, { wait: 1000 });
  await setTheme(k, 'system');

  // ================= back to the waiter =================
  await W('Waiter: ready, serve, bill', 'My orders shows Ready', 'Bottom nav Orders', `The ${TABLE} order card has the Ready status`, 'my-orders-ready', async () => {
    await nav(w, 'Orders');
    await w.locator('[data-testid=order-card][data-status=Ready]').first().waitFor({ timeout: 10000 });
  }, { wait: 1500 });
  await W('Waiter: ready, serve, bill', 'Mark served', `Open ${TABLE}, tap Mark served`, 'The Mark served action disappears and the order is served', 'served', async () => {
    await openTable(w, TABLE);
    await w.locator('.action-list [data-action=serve]').waitFor({ timeout: 8000 });
    await action(w, 'serve');
    await w.locator('.action-list [data-action=serve]').waitFor({ state: 'detached', timeout: 8000 });
  }, { wait: 1000 });
  await W('Waiter: ready, serve, bill', 'Request the bill', 'Tap Request bill', 'Banner "Bill requested"; the bill waits for the cashier (desktop chapter)', 'bill-requested', async () => {
    await action(w, 'request-bill');
    await w.locator('.banner.info', { hasText: 'Bill requested' }).waitFor({ timeout: 8000 });
  });
  await W('Waiter: ready, serve, bill', 'Bell with the ready notification', 'Tap the bell', 'Notification list shows the order-ready entry', 'bell-ready', async () => {
    await clearOverlays(w);
    await w.locator('.topbar button.bell').click();
    await bold(w, 'button[aria-label="Close notifications"]');
  });
  await w.locator('button[aria-label="Close notifications"]').click().catch(() => {});

  // ---- offline drill ----
  await W('Waiter: offline', 'Offline banner', 'Open an empty Takeaway order builder (no table needed), drop the network', 'Offline banner is shown', 'offline-banner', async () => {
    await pickChannel(w, 'picker-takeaway');
    await waiterCtx.setOffline(true);
    for (const ws of hubSockets.splice(0)) await ws.close({ code: 4000, reason: 'offline drill' }).catch(() => {});
    await w.locator('[data-testid=offline-banner]').waitFor({ timeout: 25000 });
  }, { wait: 200 });
  await W('Waiter: offline', 'My orders while offline', 'Open My orders while offline', 'Banner or an error state, no crash', 'offline-my-orders', async () => {
    await nav(w, 'Orders');
    await pause(w, 4000);
  }, { wait: 500 });
  await W('Waiter: offline', 'Back online', 'Restore the network', 'The offline banner goes away', 'back-online', async () => {
    await waiterCtx.setOffline(false);
    await w.locator('[data-testid=offline-banner]').waitFor({ state: 'detached', timeout: 25000 });
  }, { wait: 500 });
} catch (err) {
  log('ABORTED:', err.message);
  await w.screenshot({ path: path.join(OUT, '_failure-waiter.png') }).catch(() => {});
  await k.screenshot({ path: path.join(OUT, '_failure-kitchen.png') }).catch(() => {});
  rows.push({ n: ++stepNo, chapter: 'Run', step: 'Script aborted', action: '-', expected: 'Flow completes', result: 'Fail', file: '', notes: String(err.message).split('\n')[0].slice(0, 220) });
  process.exitCode = 1;
} finally {
  await waiterCtx.setOffline(false).catch(() => {});
  await saveState(waiterCtx, 'waiter').catch(() => {});
  await saveState(kitchenCtx, 'kitchen').catch(() => {});
  const videos = RECORD ? { waiter: w.video(), kitchen: k.video() } : null;
  await browser.close();
  if (videos) {
    const paths = { waiter: await videos.waiter.path(), kitchen: await videos.kitchen.path() };
    fs.writeFileSync(path.join(RECORD, 'web-record.json'), JSON.stringify({ videos: paths, steps: marks }, null, 1));
    log(`recorded ${marks.length} steps -> ${paths.waiter}, ${paths.kitchen}`);
  }

  // ---- device clean-up: deactivate the WEB-* terminals this run's browser profiles registered ----
  const cleanup = [];
  if (process.env.KEEP_DEVICES !== '1') {
    try {
      const devs = (await apiGet(adminToken, '/api/admin/devices')).data ?? [];
      const mine = myDeviceIds();
      for (const d of devs) {
        if (!d.isActive || !/^WEB-/i.test(d.name)) continue;
        if (!mine.has(d.name.toUpperCase())) { cleanup.push(`${d.name} left active (not this run's profile)`); continue; }
        const r = await apiPost(adminToken, `/api/admin/devices/${d.id}/deactivate`);
        cleanup.push(`${d.name} (${d.type}) ${r.success ? 'deactivated' : 'deactivate FAILED: ' + r.message}`);
      }
      if (!cleanup.length) cleanup.push('no active WEB-* terminal of this run found');
    } catch (e) { cleanup.push('device clean-up failed: ' + e.message); }
  }

  // ---- outputs ----
  fs.writeFileSync(path.join(OUT, 'web-small-targets.json'), JSON.stringify(small, null, 1));
  fs.writeFileSync(path.join(OUT, 'web-console-errors.json'), JSON.stringify(consoleErrors, null, 1));
  fs.writeFileSync(path.join(OUT, 'web-page-overflow.json'), JSON.stringify(overflow, null, 1));
  const pass = rows.filter(r => r.result === 'Pass').length;
  const fail = rows.length - pass;
  const esc = s => String(s ?? '').replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');
  const smallBad = Object.entries(small).filter(([, v]) => v.length);
  const md = [];
  md.push('# Web demo flow: test record', '');
  md.push(`Run ${new Date().toISOString()} against ${BASE} (Development API, owner's dev DB). Waiter on a 390x844 phone viewport, kitchen on a 1024x768 tablet viewport, installed Edge via Playwright. Main table ${TABLE}, spare table ${SPARE}. Script: docs/ui-ux/tools/demo-web-flow.mjs. Screenshots: 02-waiter-phone/ (${counters[WF]}) and 03-kitchen-tablet/ (${counters[KF]}).`, '');
  md.push('Set-up through the API (not screenshots): ' + prep.join(' '), '');
  md.push('| # | Chapter | Step | Action | Expected | Result (Pass/Fail) | Screenshot | Notes |', '|---|---|---|---|---|---|---|---|');
  for (const r of rows) md.push(`| ${r.n} | ${esc(r.chapter)} | ${esc(r.step)} | ${esc(r.action)} | ${esc(r.expected)} | ${r.result} | ${esc(r.file)} | ${esc(r.notes)} |`);
  md.push('', `**Summary: ${pass} passed / ${fail} failed / ${rows.length} total.**`, '');
  md.push('## Device clean-up', '', ...(cleanup.length ? cleanup.map(c => '- ' + c) : ['- skipped (KEEP_DEVICES=1)']), '');
  md.push('## UI/UX issues noticed (measured by the script)', '');
  md.push(`- Console errors / page errors: ${consoleErrors.length} (see web-console-errors.json).`);
  md.push(`- Screens with a visible control under ${MIN_TARGET} px: ${smallBad.length} of ${Object.keys(small).length} (see web-small-targets.json).`);
  for (const [f, v] of smallBad.slice(0, 40)) md.push(`  - ${f}: ${v.map(x => `${x.text || x.cls || x.tag} ${x.w}x${x.h}`).slice(0, 4).join('; ')}${v.length > 4 ? ` (+${v.length - 4})` : ''}`);
  for (const [f, v] of Object.entries(overflow)) md.push(`- Horizontal overflow on ${f}: ${v.wide.join(', ')}`);
  md.push('', '## UI/UX issues noticed (by eye)', '', '<!-- MANUAL-ISSUES -->', '');
  fs.writeFileSync(path.join(OUT, 'web-test-record.md'), md.join('\n'));
  log(`done: ${pass} passed, ${fail} failed, ${rows.length} total; screenshots ${counters[WF]} + ${counters[KF]}`);
}
