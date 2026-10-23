// HotelPOS web client: waiter + kitchen flow screenshots for the UI/UX gallery (docs/ui-ux/web).
// Phase 21.5: selectors follow the Phase 21 markup (five-tab waiter shell with More / pick-a-table sheets, TableCard,
// MenuItemCard + CartBar + review sheet, ItemOptionsSheet with the note inside, Details/Items/Timeline tabs, ActionList
// [data-action], kitchen KitchenHeader + KPI strip + TicketCard [data-action] + TicketDetailPane).
//
// How to run (Playwright is NOT installed in the repo; keep node_modules out of git):
//   1. Start the API in Development mode (the PWA is served at /app) with the demo seed.
//   2. In any scratch folder OUTSIDE the repo:  npm init -y && npm i playwright
//   3. From that scratch folder:  node <repo>/docs/ui-ux/tools/web-flow.mjs
//      (Playwright is resolved from the current folder; uses the installed Edge via channel 'msedge', no browser download.)
// Env: START_AT=kitchen|offline (resume at that part), HOTELPOS_URL (default http://localhost:5000/app/; the 20.11 and
//      21.5 runs used http://localhost:5100/app/), HOTELPOS_TABLE (default T04; must be free — the script falls back to
//      the first free table of the pick-a-table sheet), HOTELPOS_SPARE_TABLE (default T02, only used to open an empty
//      order builder for the offline drill), PW_CHANNEL (msedge | chrome), OUT_DIR.
// STATE_DIR (default <tmp>/hotelpos-web-flow) keeps the browser device identity so reruns do not use new licence seats.
// Demo accounts: waiter1 / Pass@123, kitchen1 / Pass@123. The flow ends with the order served and the bill requested.
// It also writes small-targets.json (visible buttons/links/inputs under 48 px in either dimension, per screenshot; the
// Phase 21 plan asks for 48 px) and console-errors.json (console errors and page errors per role).
import { createRequire } from 'node:module';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import os from 'node:os';

const require = createRequire(path.join(process.cwd(), 'noop.js'));
const { chromium } = require('playwright');

const here = path.dirname(fileURLToPath(import.meta.url));
const BASE = process.env.HOTELPOS_URL ?? 'http://localhost:5000/app/';
let TABLE = process.env.HOTELPOS_TABLE ?? 'T04';
const SPARE = process.env.HOTELPOS_SPARE_TABLE ?? 'T02';
const OUT = process.env.OUT_DIR ?? path.resolve(here, '..', 'web');
const START_AT = process.env.START_AT ?? 'waiter'; // 'kitchen' resumes after the waiter has sent the order; 'offline' runs only the offline drill
const CHANNEL = process.env.PW_CHANNEL ?? 'msedge';
const MIN_TARGET = 48;
fs.mkdirSync(path.join(OUT, 'waiter'), { recursive: true });
fs.mkdirSync(path.join(OUT, 'kitchen'), { recursive: true });
// Every new browser profile registers a new terminal and uses a licence seat (README issue). The device identity lives
// in localStorage, so it is kept between runs here (cookies are dropped, so each run still signs in).
const STATE_DIR = process.env.STATE_DIR ?? path.join(os.tmpdir(), 'hotelpos-web-flow');
fs.mkdirSync(STATE_DIR, { recursive: true });
const stateFile = role => path.join(STATE_DIR, role + '.json');
const stateFor = role => (fs.existsSync(stateFile(role)) ? stateFile(role) : undefined);
async function saveState(ctx, role) {
  const st = await ctx.storageState();
  st.cookies = [];
  fs.writeFileSync(stateFile(role), JSON.stringify(st));
}

const small = {};
const overflow = {};
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
const pause = (p, ms = 700) => p.waitForTimeout(ms);

async function shot(page, name, opts = {}) {
  await pause(page, opts.wait ?? 800);
  // The role is the second word of the name ("07-waiter-item-options" is a waiter screen).
  await page.screenshot({ path: path.join(OUT, name.split('-')[1] === 'kitchen' ? 'kitchen' : 'waiter', `${name}.png`), fullPage: !!opts.full });
  small[name] = await page.evaluate(min => {
    const out = [];
    for (const el of document.querySelectorAll('button, a[href], input, textarea, [role=button]')) {
      const r = el.getBoundingClientRect();
      const st = getComputedStyle(el);
      if (r.width === 0 || r.height === 0 || st.visibility === 'hidden') continue;
      if (r.bottom < 0 || r.top > innerHeight) continue;
      if (el.type === 'checkbox' || r.width < min || r.height < min) {
        out.push({
          tag: el.tagName.toLowerCase(),
          cls: (el.className?.toString() ?? '').slice(0, 40),
          text: (el.innerText || el.getAttribute('aria-label') || el.type || '').trim().slice(0, 30),
          w: Math.round(r.width), h: Math.round(r.height),
        });
      }
    }
    return out;
  }, MIN_TARGET);
  // Horizontal overflow: mobile Chromium zooms the whole page out when any element is wider than the viewport
  // (layout viewport > visual viewport), which also breaks tap coordinates. Recorded per shot in page-overflow.json.
  const ov = await page.evaluate(() => {
    const vw = visualViewport.width;
    if (document.documentElement.scrollWidth <= Math.ceil(vw) && innerWidth <= Math.ceil(vw)) return null;
    const wide = [];
    for (const el of document.querySelectorAll('body *')) {
      const r = el.getBoundingClientRect();
      if (r.right > vw + 1 && r.width > 0 && !el.closest('.chip-row, .cart-thumbs, .kds-stations')) wide.push(`${el.tagName.toLowerCase()}.${(el.className?.baseVal ?? el.className ?? '').toString().trim().split(/\s+/)[0]} right=${Math.round(r.right)}`);
    }
    return { innerWidth, visualWidth: Math.round(vw), scrollWidth: document.documentElement.scrollWidth, wide: wide.slice(0, 8) };
  });
  if (ov) { overflow[name] = ov; log('  horizontal overflow:', JSON.stringify(ov)); }
  log('shot', name);
}

// A fresh toast sits over the bottom of the screen for 3 s; a user taps it away, so the script does too before using
// a bottom sheet or the bottom nav.
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

// Waiter bottom nav: Home / Tables / Orders are links; "New order" and "More" are buttons that open sheets.
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

// Kitchen: the primary navigation sits in the top bar (nav.headnav) on a tablet.
async function kGo(page, label) {
  await clearOverlays(page);
  await page.locator(`nav.headnav a[aria-label="${label}"]`).click();
  await page.locator('.skeleton-group').waitFor({ state: 'detached', timeout: 15000 }).catch(() => {});
  await pause(page, 900);
}

async function openTable(page, code) {
  await nav(page, 'Tables');
  await page.locator('.table-card').first().waitFor();
  await page.locator('.table-card', { has: page.locator('.table-code', { hasText: new RegExp(`^${code}$`) }) }).click();
  await page.locator('.detail-card h1').first().waitFor();
  await pause(page, 900);
}

async function action(page, key) {
  await clearOverlays(page);
  await page.locator(`.action-list [data-action="${key}"]`).click();
}

// Choose the first option of every modifier group (enough for "Choose 1"; optional groups get one too). The quick-note
// chips live in their own .options.quick-notes row and are not modifier options.
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

async function setTheme(page, mode) {
  // Phase 25 pass 4: on waiter phones the theme switch lives in the More sheet, not the top bar.
  let toggle = page.locator('.topbar .theme-toggle');
  const inSheet = !(await toggle.isVisible());
  if (inSheet) {
    await navSheet(page, 'More');
    toggle = page.locator('.overlay .sheet .theme-toggle');
  }
  for (let i = 0; i < 3 && !(await toggle.getAttribute('title')).includes(mode); i++) {
    await toggle.click();
    await pause(page, 200);
  }
  if (inSheet) await closeSheet(page);
}

const browser = await chromium.launch({ channel: CHANNEL, headless: true });
const common = { serviceWorkers: 'block', deviceScaleFactor: 2, hasTouch: true, colorScheme: 'light', locale: 'en-IN', timezoneId: 'Asia/Kolkata' };
const waiterCtx = await browser.newContext({ ...common, viewport: { width: 390, height: 844 }, isMobile: true, storageState: stateFor('waiter') });
const kitchenCtx = await browser.newContext({ ...common, viewport: { width: 1024, height: 768 }, storageState: stateFor('kitchen') });
// Workaround for README issue 3 (fixed in 20.11: index.html now asks for ../_content/...). Kept so older builds still
// render; it matches nothing now.
if (process.env.HOTELPOS_NO_CSS_FIX !== '1') {
  for (const ctx of [waiterCtx, kitchenCtx]) {
    await ctx.route('**/app/_content/**', r => r.continue({ url: r.request().url().replace('/app/_content/', '/_content/') }));
  }
}
const hubSockets = [];
await waiterCtx.routeWebSocket(/\/hub/, ws => { ws.connectToServer(); hubSockets.push(ws); });
const w = await waiterCtx.newPage();
const k = await kitchenCtx.newPage();
const consoleErrors = [];
for (const [role, p] of [['waiter', w], ['kitchen', k]]) {
  p.setDefaultTimeout(20000);
  p.on('console', m => { if (m.type() === 'error') consoleErrors.push({ role, url: p.url(), text: m.text().slice(0, 400) }); });
  p.on('pageerror', e => consoleErrors.push({ role, url: p.url(), text: 'pageerror: ' + e.message.slice(0, 400) }));
}

try {
  // ---------------- Waiter ----------------
  if (START_AT !== 'waiter') {
    // resume after a failure in the kitchen part: the order is already sent; sign the waiter in silently
    await signIn(w, 'Waiter', 'waiter1');
    await w.locator('button[type=submit]').click();
    await w.waitForURL(/\/home|\/tables/);
  } else {
  await signIn(w, 'Waiter', 'waiter1');
  await shot(w, '01-waiter-sign-in');
  await w.locator('button[type=submit]').click();
  await w.waitForURL(/\/home|\/tables/);
  await w.locator('#home-my-tables').waitFor().catch(() => {});
  await shot(w, '02-waiter-home', { wait: 1500 });

  await nav(w, 'Tables');
  await w.locator('.table-card').first().waitFor();
  await shot(w, '03-waiter-tables', { wait: 1200 });

  // the "New order" tab opens the pick-a-table sheet; make sure the flow's table is free, else take the first free one
  await navSheet(w, 'New order');
  await w.locator('[data-testid=picker-available] .picker-table, [data-testid=picker-available] p').first().waitFor();
  await shot(w, '03b-waiter-pick-table');
  const free = await w.locator('[data-testid=picker-available] .picker-table b').allInnerTexts();
  if (!free.includes(TABLE)) {
    log(`${TABLE} is not free; using ${free[0] ?? '(none)'}`);
    TABLE = free[0] ?? TABLE;
  }
  await closeSheet(w);

  await openTable(w, TABLE);
  if (await w.locator('[data-action=occupy]').count()) {
    const value = async () => parseInt(await w.locator('.stepper-value').first().innerText(), 10);
    while ((await value()) < 2) await w.locator('.stepper button[aria-label$="one more"]').first().click();
    while ((await value()) > 2) await w.locator('.stepper button[aria-label$="one less"]').first().click();
    await shot(w, '04-waiter-table-details');
    await w.locator('[data-action=occupy]').click();
    await w.locator('.action-list').waitFor();
    await pause(w, 1200);
    await shot(w, '04b-waiter-table-occupied');
  } else {
    await shot(w, '04b-waiter-table-occupied');
  }

  await action(w, 'new-order');
  await w.locator('#menu-search').waitFor();
  await pause(w, 800);
  if (await w.locator('.banner.info', { hasText: 'restored' }).count()) {
    await shot(w, '04c-waiter-draft-restored');
    // start from an empty cart so the gallery shows a clean 3-line order
    await w.locator('[data-testid=cart-review]').click();
    for (let i = 0; i < 40 && await w.locator('.cart-line').count(); i++) {
      await w.locator('.cart-line [data-testid=cart-line-delete]').first().click();
      await pause(w, 150);
    }
    await closeSheet(w);
  }
  await w.locator('#menu-search').fill('biry');
  await pause(w, 600);
  // pin two items through the star (skip ones already pinned)
  const stars = w.locator('.item-wrap .item-star');
  const starCount = await stars.count();
  for (let i = 0; i < Math.min(2, starCount); i++) {
    if ((await stars.nth(i).getAttribute('aria-pressed')) !== 'true') await stars.nth(i).click();
    await pause(w, 200);
  }
  await shot(w, '05-waiter-order-search');

  await w.locator('.search-clear').click();
  await pause(w, 400);
  await w.locator('#chip-favourites').click();
  await shot(w, '06-waiter-favourites');

  // item 1: a favourite with options -> options sheet (options + quick notes + note + quantity in one sheet, D2)
  const optCard = w.locator('.item-wrap', { hasText: '+ options' }).locator('.item-card:not([disabled])').first();
  await optCard.click();
  await w.locator('.overlay .sheet').waitFor();
  await satisfyPicker(w);
  await w.locator('.quick-notes button', { hasText: 'Less oil' }).click().catch(() => {});
  await w.locator('[data-testid=item-note]').fill('Less oil, extra raita on the side');
  await shot(w, '07-waiter-item-options');
  await w.locator('[data-testid=options-confirm]').click();
  await w.locator('.overlay').waitFor({ state: 'detached', timeout: 5000 }).catch(() => {});
  await pause(w, 500);

  // item 2: a bread (quantity 2 in the review sheet), item 3: a beverage
  await w.locator('.chip-btn', { hasText: 'Breads' }).click();
  await pause(w, 500);
  await addItem(w, w.locator('.item-card:not([disabled])').first());
  await w.locator('.chip-btn', { hasText: 'Beverages' }).click();
  await pause(w, 500);
  await addItem(w, w.locator('.item-card:not([disabled])').first());
  await w.locator('.cart-bar').scrollIntoViewIfNeeded();
  await shot(w, '08-waiter-cart');

  // review sheet: every line with its controls, guests, order notes, Send
  await clearOverlays(w);
  await w.locator('[data-testid=cart-review]').click();
  await w.locator('.overlay .sheet .cart-line').first().waitFor();
  await w.locator('.cart-line').nth(1).locator('button[aria-label="One more"]').click();
  await pause(w, 300);
  await w.locator('[data-testid=order-notes]').fill('Birthday table — bring the dessert last');
  await w.locator('[data-testid=order-notes]').blur();
  await pause(w, 300);
  await shot(w, '08b-waiter-review-sheet');

  await w.locator('[data-testid=send]').click();
  await w.waitForURL(/\/tables\/\d+$/, { timeout: 10000 });
  await w.locator('.detail-card h1').first().waitFor();
  await pause(w, 400);
  await shot(w, '09-waiter-after-send', { wait: 600 });
  await w.locator('.tabs .tab', { hasText: 'Items' }).click();
  await shot(w, '09c-waiter-order-items');
  await w.locator('.tabs .tab', { hasText: 'Timeline' }).click();
  await w.locator('.skeleton-group').waitFor({ state: 'detached', timeout: 10000 }).catch(() => {});
  await shot(w, '09d-waiter-order-timeline');

  await nav(w, 'Orders');
  await w.locator('[data-testid=order-card]').first().waitFor();
  await shot(w, '09b-waiter-my-orders', { wait: 1200 });
  await w.locator('.tabs .tab', { hasText: 'Completed' }).click();
  await w.locator('.skeleton-group').waitFor({ state: 'detached', timeout: 10000 }).catch(() => {});
  await shot(w, '09e-waiter-my-orders-completed');
  await w.locator('.tabs .tab', { hasText: 'Active' }).click();

  await clearOverlays(w);
  await w.locator('.topbar button.bell').click();
  await shot(w, '10-waiter-bell-panel');
  await w.locator('button[aria-label="Close notifications"]').click();

  await navSheet(w, 'More');
  await shot(w, '10b-waiter-more-sheet');
  await w.locator('.sheet a.more-link', { hasText: 'Settings' }).click();
  await w.waitForURL(/\/settings/);
  await shot(w, '11-waiter-settings', { wait: 1000 });

  await nav(w, 'Tables');
  await w.locator('.table-card').first().waitFor();
  await setTheme(w, 'dark');
  await shot(w, '12-waiter-tables-dark', { wait: 1000 });
  await setTheme(w, 'system');

  }

  if (START_AT !== 'offline') {
  // ---------------- Kitchen ----------------
  await signIn(k, 'Kitchen', 'kitchen1');
  await shot(k, '13-kitchen-sign-in');
  await k.locator('button[type=submit]').click();
  await k.waitForURL(/\/kitchen/);
  await pause(k, 1500);
  let stationShot = false;
  if (k.url().includes('/kitchen/station')) {
    await shot(k, '15-kitchen-station');
    stationShot = true;
    await k.locator('.card button', { hasText: 'All stations' }).click();
    await k.waitForURL(/\/kitchen$/);
  }
  const t = k.locator('article.ticket', { has: k.locator('.ticket-table', { hasText: new RegExp('^' + TABLE + '$') }) });
  await t.first().waitFor();
  log('kitchen head nav links:', await k.locator('nav.headnav a').count(), '| tickets for', TABLE + ':', await t.count());
  await shot(k, '14-kitchen-board-new', { wait: 1200 });
  // ticket detail pane (right drawer at 1024 px): tap the ticket head, not a button
  await t.first().locator('.ticket-top').click();
  await k.locator('[data-testid=ticket-detail]').waitFor();
  await k.locator('[data-testid=guests]').waitFor({ timeout: 5000 }).catch(() => {});
  await shot(k, '14b-kitchen-ticket-detail');
  await k.locator('.detail-pane button[aria-label="Close"]').click();
  await k.locator('.detail-overlay').waitFor({ state: 'detached' });
  // Delayed filter on (the older seeded tickets are late; this run's ticket is not)
  await k.locator('#kds-delayed-filter button').click();
  await shot(k, '14c-kitchen-delayed-filter');
  await k.locator('#kds-delayed-filter button').click();
  await pause(k, 400);
  if (!stationShot) {
    await kGo(k, 'Station');
    await shot(k, '15-kitchen-station');
    await kGo(k, 'Kitchen');
    await t.first().waitFor();
  }
  // press a button on every ticket of the table (one per station), re-reading the board after each press
  async function pressAll(act) {
    for (let i = 0; i < 10; i++) {
      const btn = t.locator(`button[data-action="${act}"]`).first();
      if (!(await btn.count())) return;
      await clearOverlays(k);
      await btn.click();
      await pause(k, 1200);
    }
  }
  await pressAll('accept');
  await shot(k, '16a-kitchen-accepted');
  await pressAll('start');
  await k.evaluate(() => scrollTo(0, 0));
  await shot(k, '16-kitchen-preparing');
  await pressAll('ready');
  await k.evaluate(() => scrollTo(0, 0));
  await shot(k, '17-kitchen-ready');
  await kGo(k, 'Completed');
  await shot(k, '18-kitchen-completed', { wait: 1200 });
  await kGo(k, 'Kitchen');
  await t.first().waitFor();
  await clearOverlays(k);
  await k.locator('.topbar button.bell').click();
  await shot(k, '19-kitchen-bell-panel');
  await k.locator('button[aria-label="Close notifications"]').click();
  await setTheme(k, 'dark');
  await shot(k, '20-kitchen-dark', { wait: 1000 });
  await setTheme(k, 'system');

  // ---------------- Waiter again ----------------
  await nav(w, 'Orders');
  await w.locator('[data-testid=order-card]').first().waitFor();
  await shot(w, '21-waiter-my-orders-ready', { wait: 1500 });
  await openTable(w, TABLE);
  await shot(w, '22a-waiter-table-ready');
  if (await w.locator('.action-list [data-action=serve]').count()) {
    await action(w, 'serve');
    await w.locator('.action-list [data-action=serve]').waitFor({ state: 'detached', timeout: 8000 }).catch(() => {});
    await shot(w, '22b-waiter-table-served', { wait: 1000 });
  }
  await action(w, 'request-bill');
  await w.locator('.banner.info', { hasText: 'Bill requested' }).waitFor({ timeout: 8000 });
  await shot(w, '22-waiter-bill-requested');
  await clearOverlays(w);
  await w.locator('.topbar button.bell').click();
  await shot(w, '23-waiter-bell-ready');
  await w.locator('button[aria-label="Close notifications"]').click();

  }

  // offline banner on an empty order builder of a spare table (nothing is saved or sent)
  await openTable(w, SPARE);
  if (await w.locator('.action-list [data-action=new-order]').count()) {
    await action(w, 'new-order');
    await w.locator('#menu-search').waitFor();
    // Chromium's offline emulation keeps an open WebSocket alive, so also drop the hub socket (proxied above);
    // SignalR then reconnects against an offline network, as on a phone that loses Wi-Fi.
    await waiterCtx.setOffline(true);
    for (const ws of hubSockets.splice(0)) await ws.close({ code: 4000, reason: 'offline drill' }).catch(() => {});
    const banner = w.locator('[data-testid=offline-banner]');
    const bannerShown = await banner.waitFor({ timeout: 20000 }).then(() => true, () => false);
    if (!bannerShown) {
      log('offline banner did not appear within 20 s');
      await shot(w, '24a-waiter-offline-no-banner-yet', { wait: 200 });
    }
    await shot(w, '24-waiter-offline-banner');
    // a natural error state: open My orders while offline
    await nav(w, 'Orders');
    await pause(w, 4000);
    await shot(w, '25-waiter-offline-my-orders', { wait: 500 });
    await waiterCtx.setOffline(false);
    await pause(w, 8000);
    await shot(w, '24b-waiter-back-online', { wait: 500 });
  } else {
    log(`${SPARE} is not free; offline banner skipped`);
  }
} catch (err) {
  log('FAILED:', err.message);
  await w.screenshot({ path: path.join(OUT, '_failure-waiter.png') }).catch(() => {});
  await k.screenshot({ path: path.join(OUT, '_failure-kitchen.png') }).catch(() => {});
  process.exitCode = 1;
} finally {
  await saveState(waiterCtx, 'waiter').catch(() => {});
  await saveState(kitchenCtx, 'kitchen').catch(() => {});
  // merged with earlier (partial, START_AT) runs so a resumed run keeps the earlier measurements
  const merge = (file, add) => {
    let old; try { old = JSON.parse(fs.readFileSync(path.join(OUT, file), 'utf8')); } catch { old = null; }
    const value = Array.isArray(add) ? [...(START_AT === 'waiter' ? [] : old ?? []), ...add] : { ...(START_AT === 'waiter' ? {} : old ?? {}), ...add };
    fs.writeFileSync(path.join(OUT, file), JSON.stringify(value, null, 1));
  };
  merge('small-targets.json', small);
  merge('console-errors.json', consoleErrors);
  merge('page-overflow.json', overflow);
  await browser.close();
}
