// HotelPOS web client: waiter + kitchen flow screenshots for the UI/UX gallery (docs/ui-ux/web).
//
// How to run (Playwright is NOT installed in the repo; keep node_modules out of git):
//   1. Start the API in Development mode on http://localhost:5000 (the PWA is served at /app) with the demo seed.
//   2. In any scratch folder OUTSIDE the repo:  npm init -y && npm i playwright
//   3. From that scratch folder:  node <repo>/docs/ui-ux/tools/web-flow.mjs
//      (Playwright is resolved from the current folder; uses the installed Edge via channel 'msedge', no browser download.)
// Env: START_AT=kitchen|offline (resume at that part), HOTELPOS_URL (default http://localhost:5000/app/; the 20.11 run used
//      http://localhost:5100/app/ from a worktree's API), HOTELPOS_TABLE (default T05), HOTELPOS_SPARE_TABLE (default T06,
//      only used to open an empty order builder for the offline banner), PW_CHANNEL (msedge | chrome), OUT_DIR.
// The "stuck" shots (07c, 09a, 22b, 24a) are taken only when the defect shows; after the Phase 20.11 fixes they are skipped.
// STATE_DIR (default <tmp>/hotelpos-web-flow) keeps the browser device identity so reruns do not use new licence seats.
// Demo accounts: waiter1 / Pass@123, kitchen1 / Pass@123. The flow ends with the bill requested on the table.
// It also writes small-targets.json: visible buttons/links/inputs under 44 px in either dimension, per screenshot.
import { createRequire } from 'node:module';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import os from 'node:os';

const require = createRequire(path.join(process.cwd(), 'noop.js'));
const { chromium } = require('playwright');

const here = path.dirname(fileURLToPath(import.meta.url));
const BASE = process.env.HOTELPOS_URL ?? 'http://localhost:5000/app/';
const TABLE = process.env.HOTELPOS_TABLE ?? 'T05';
const SPARE = process.env.HOTELPOS_SPARE_TABLE ?? 'T06';
const OUT = process.env.OUT_DIR ?? path.resolve(here, '..', 'web');
const START_AT = process.env.START_AT ?? 'waiter'; // 'kitchen' resumes after the waiter has sent the order; 'offline' runs only the offline drill
const CHANNEL = process.env.PW_CHANNEL ?? 'msedge';
fs.mkdirSync(path.join(OUT, "waiter"), { recursive: true });
fs.mkdirSync(path.join(OUT, "kitchen"), { recursive: true });
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
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
const pause = (p, ms = 700) => p.waitForTimeout(ms);

async function shot(page, name, opts = {}) {
  await pause(page, opts.wait ?? 800);
  // The role is the second word of the name ("07b-waiter-kitchen-note" is a waiter screen).
  await page.screenshot({ path: path.join(OUT, name.split('-')[1] === 'kitchen' ? 'kitchen' : 'waiter', `${name}.png`), fullPage: !!opts.full });
  small[name] = await page.evaluate(() => {
    const out = [];
    for (const el of document.querySelectorAll('button, a[href], input, textarea, [role=button]')) {
      const r = el.getBoundingClientRect();
      const st = getComputedStyle(el);
      if (r.width === 0 || r.height === 0 || st.visibility === 'hidden') continue;
      if (r.bottom < 0 || r.top > innerHeight) continue;
      if (el.type === 'checkbox' || r.width < 44 || r.height < 44) {
        out.push({
          tag: el.tagName.toLowerCase(),
          cls: (el.className?.toString() ?? '').slice(0, 40),
          text: (el.innerText || el.getAttribute('aria-label') || el.type || '').trim().slice(0, 30),
          w: Math.round(r.width), h: Math.round(r.height),
        });
      }
    }
    return out;
  });
  log('shot', name);
}

// Toasts never auto-dismiss in the browser build (README issue 2) and the Blazor error bar covers the bottom of the
// screen; a user taps them away, so the script does too before using a bottom sheet.
async function clearOverlays(page) {
  for (const t of await page.locator('.toasts .toast').all()) await t.click().catch(() => {});
  const dismiss = page.locator('#blazor-error-ui .dismiss');
  if (await dismiss.isVisible().catch(() => false)) await dismiss.click().catch(() => {});
  await pause(page, 200);
}

async function signIn(page, role, user) {
  await page.goto(BASE + 'signin', { waitUntil: 'domcontentloaded' });
  await page.locator('.signin .card').waitFor();
  await page.locator('.seg button', { hasText: role }).click();
  await page.locator('input[autocomplete=username]').fill(user);
  await page.locator('input[type=password]').fill('Pass@123');
  await page.keyboard.press('Tab'); // @bind updates on change; the Sign in button stays disabled until the field blurs
  await pause(page, 300);
}

async function nav(page, label) {
  await clearOverlays(page);
  await page.locator('nav.bottomnav a', { hasText: label }).first().click();
  await pause(page, 900);
}

// Before Phase 20.11 the kitchen bottom nav was missing (README issue 2); fall back to the URL like a user typing it.
async function kGo(page, label, rel) {
  const link = page.locator('nav.bottomnav a', { hasText: label });
  if (await link.count()) await nav(page, label);
  else {
    await page.goto(BASE + rel);
    await page.locator('.topbar').waitFor();
  }
  await page.locator('.skeleton-group').waitFor({ state: 'detached', timeout: 15000 }).catch(() => {});
  await pause(page, 800);
}

async function openTable(page, code) {
  await nav(page, 'Tables');
  await page.locator('button.tile').first().waitFor();
  await page.locator('button.tile', { has: page.locator('.tile-code', { hasText: new RegExp(`^${code}$`) }) }).click();
  await page.locator('.card h1').first().waitFor();
  await pause(page, 900);
}

async function satisfyPicker(page) {
  await clearOverlays(page);
  // choose the first option of every group (enough for "Choose 1"; optional groups get one too)
  const groups = page.locator('.sheet .options');
  const n = await groups.count();
  for (let i = 0; i < n; i++) {
    const first = groups.nth(i).locator('button:not([disabled])').first();
    if (await first.count()) await first.click();
  }
}

// README issue 2: every toast throws in the browser build (TaskScheduler.FromCurrentSynchronizationContext), so the
// handler that raised it never re-renders: the sheet stays open / the cart looks unchanged until the next tap.
// A waiter taps again; the script does the same: tap the dimmed backdrop, or the selected category chip.
async function nudge(page) {
  await pause(page, 300);
  const overlay = page.locator('.overlay');
  if (await overlay.count()) await overlay.first().click({ position: { x: 20, y: 20 } }).catch(() => {});
  else if (await page.locator('.chip-btn.on').count()) await page.locator('.chip-btn.on').first().click().catch(() => {});
  await pause(page, 400);
}

async function addItem(page, card) {
  await clearOverlays(page);
  await card.click();
  await pause(page, 500);
  if (await page.locator('.overlay .sheet').count()) {
    await satisfyPicker(page);
    await page.locator('.sheet button', { hasText: 'Add to order' }).click();
  }
  await nudge(page);
}

async function setTheme(page, mode) {
  const toggle = page.locator('.theme-toggle');
  for (let i = 0; i < 3 && !(await toggle.getAttribute('title')).includes(mode); i++) {
    await toggle.click();
    await pause(page, 200);
  }
}

const browser = await chromium.launch({ channel: CHANNEL, headless: true });
const common = { serviceWorkers: 'block', deviceScaleFactor: 2, hasTouch: true, colorScheme: 'light', locale: 'en-IN', timezoneId: 'Asia/Kolkata' };
const waiterCtx = await browser.newContext({ ...common, viewport: { width: 390, height: 844 }, isMobile: true, storageState: stateFor('waiter') });
const kitchenCtx = await browser.newContext({ ...common, viewport: { width: 1024, height: 768 }, storageState: stateFor('kitchen') });
// Workaround for README issue 3 (fixed in 20.11: index.html now asks for ../_content/...): the Client.Shared stylesheets
// are served at /_content/..., and /app/_content/... answered 401. Kept so older builds still render; it matches nothing now.
if (process.env.HOTELPOS_NO_CSS_FIX !== '1') {
  for (const ctx of [waiterCtx, kitchenCtx]) {
    await ctx.route('**/app/_content/**', r => r.continue({ url: r.request().url().replace('/app/_content/', '/_content/') }));
  }
}
const hubSockets = [];
await waiterCtx.routeWebSocket(/\/hub/,ws => { ws.connectToServer(); hubSockets.push(ws); });
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
  await w.locator('button.tile').first().waitFor();
  await shot(w, '03-waiter-tables', { wait: 1200 });

  await openTable(w, TABLE);
  if (await w.locator('button', { hasText: 'OCCUPY' }).count()) {
    const value = async () => parseInt(await w.locator('.stepper-value').first().innerText(), 10);
    while ((await value()) < 2) await w.locator('.stepper button[aria-label$="one more"]').first().click();
    while ((await value()) > 2) await w.locator('.stepper button[aria-label$="one less"]').first().click();
    await shot(w, '04-waiter-table-details');
    await w.locator('button', { hasText: 'OCCUPY' }).click();
    await pause(w, 1200);
    await shot(w, '04b-waiter-table-occupied');
  } else {
    await shot(w, '04b-waiter-table-occupied');
  }

  await w.locator('button', { hasText: /NEW ORDER|RESUME ORDER/ }).click();
  await w.locator('#menu-search').waitFor();
  await pause(w, 800);
  if (await w.locator('.banner.info', { hasText: 'restored' }).count()) {
    await shot(w, '04c-waiter-draft-restored');
    // start from an empty cart so the gallery shows a clean 3-line order
    for (let i = 0; i < 40 && await w.locator('.cart-line').count(); i++) {
      await w.locator('.cart-line button[aria-label="One less"]').first().click();
      await pause(w, 150);
    }
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

  await w.locator('#menu-search').fill('');
  await pause(w, 400);
  await w.locator('#chip-favourites').click();
  await shot(w, '06-waiter-favourites');

  // item 1: a favourite with options -> options sheet
  const optCard = w.locator('.item-wrap', { hasText: '+ options' }).locator('.item-card:not([disabled])').first();
  await optCard.click();
  await w.locator('.overlay .sheet').waitFor();
  await satisfyPicker(w);
  await shot(w, '07-waiter-item-options');
  await w.locator('.sheet button', { hasText: 'Add to order' }).click();
  await pause(w, 1000);
  if (await w.locator('.overlay .sheet').count()) await shot(w, '07c-waiter-sheet-stuck-after-add', { wait: 200 });
  await nudge(w);
  // note on that line
  await clearOverlays(w);
  await w.locator('.cart-line button[aria-label="Note for the kitchen"]').first().click();
  await w.locator('.sheet textarea').waitFor();
  await w.locator('.sheet .options button', { hasText: 'Less oil' }).click().catch(() => {});
  await w.locator('.sheet textarea').fill('Less oil, extra raita on the side');
  await shot(w, '07b-waiter-kitchen-note');
  await w.locator('.sheet button', { hasText: 'Save note' }).click();
  await pause(w, 400);
  if (await w.locator('.overlay .sheet').count()) await nudge(w);

  // item 2: a bread (quantity 2), item 3: a beverage
  await w.locator('.chip-btn', { hasText: 'Breads' }).click();
  await pause(w, 500);
  await addItem(w, w.locator('.item-card:not([disabled])').first());
  await w.locator('.cart-line').last().locator('button[aria-label="One more"]').click();
  await pause(w, 300);
  await w.locator('.chip-btn', { hasText: 'Beverages' }).click();
  await pause(w, 500);
  await addItem(w, w.locator('.item-card:not([disabled])').first());
  await w.locator('.cart').scrollIntoViewIfNeeded();
  await shot(w, '08-waiter-cart');
  await shot(w, '08b-waiter-cart-fullpage', { full: true, wait: 300 });

  await clearOverlays(w);
  await w.locator('.cart button', { hasText: 'SEND TO KITCHEN' }).click();
  const navigated = await w.waitForURL(/\/tables\/\d+$/, { timeout: 6000 }).then(() => true, () => false);
  if (!navigated) {
    // the order is sent, but the page stays on the builder (README issue 2)
    await shot(w, '09a-waiter-send-stuck-on-builder', { wait: 300 });
    await openTable(w, TABLE);
  }
  await pause(w, 400);
  await shot(w, '09-waiter-after-send', { wait: 600 });
  await nav(w, 'My orders');
  await shot(w, '09b-waiter-my-orders', { wait: 1200 });

  await clearOverlays(w);
  await w.locator('button.bell').click();
  await shot(w, '10-waiter-bell-panel');
  await w.locator('button[aria-label="Close notifications"]').click();

  await nav(w, 'Settings');
  await shot(w, '11-waiter-settings');

  await nav(w, 'Tables');
  await w.locator('button.tile').first().waitFor();
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
  const t05 = k.locator('article.ticket', { has: k.locator('.ticket-table', { hasText: new RegExp('^' + TABLE + '$') }) });
  await t05.first().waitFor();
  log('kitchen bottom nav links:', await k.locator('nav.bottomnav a').count(), '| tickets for', TABLE + ':', await t05.count());
  await shot(k, '14-kitchen-board-new', { wait: 1200 });
  await t05.last().scrollIntoViewIfNeeded();
  await shot(k, '14b-kitchen-board-new-scrolled', { wait: 300 });
  if (!stationShot) {
    await kGo(k, 'Station', 'kitchen/station');
    await shot(k, '15-kitchen-station');
    await kGo(k, 'Kitchen', 'kitchen');
    await t05.first().waitFor();
  }
  // press a button on every ticket of the table (one per station), re-reading the board after each press
  async function pressAll(label) {
    for (let i = 0; i < 10; i++) {
      const btn = t05.locator('button', { hasText: new RegExp('^' + label + '$') }).first();
      if (!(await btn.count())) return;
      await clearOverlays(k);
      await btn.click();
      await pause(k, 1200);
    }
  }
  await pressAll('ACCEPT');
  await t05.last().scrollIntoViewIfNeeded().catch(() => {});
  await shot(k, '16a-kitchen-accepted');
  await pressAll('START');
  await k.evaluate(() => scrollTo(0, 0));
  await shot(k, '16-kitchen-preparing');
  await pressAll('READY');
  await k.evaluate(() => scrollTo(0, 0));
  await shot(k, '17-kitchen-ready');
  await kGo(k, 'Completed', 'kitchen/completed');
  await shot(k, '18-kitchen-completed', { wait: 1200 });
  await kGo(k, 'Kitchen', 'kitchen');
  await t05.first().waitFor();
  await clearOverlays(k);
  await k.locator('button.bell').click();
  await shot(k, '19-kitchen-bell-panel');
  await k.locator('button[aria-label="Close notifications"]').click();
  await setTheme(k, 'dark');
  await shot(k, '20-kitchen-dark', { wait: 1000 });
  await setTheme(k, 'system');

  // ---------------- Waiter again ----------------
  await nav(w, 'My orders');
  await shot(w, '21-waiter-my-orders-ready', { wait: 1500 });
  await openTable(w, TABLE);
  await shot(w, '22a-waiter-table-ready');
  await clearOverlays(w);
  await w.locator('button', { hasText: 'REQUEST BILL' }).click();
  const billBanner = w.locator('.banner.info', { hasText: 'Bill requested' });
  const shown = await billBanner.waitFor({ timeout: 5000 }).then(() => true, () => false);
  if (!shown) {
    await shot(w, '22b-waiter-bill-request-stale', { wait: 200 });
    await openTable(w, TABLE);
  }
  await shot(w, '22-waiter-bill-requested');
  await clearOverlays(w);
  await w.locator('button.bell').click();
  await shot(w, '23-waiter-bell-ready');
  await w.locator('button[aria-label="Close notifications"]').click();

  }

  // offline banner on an empty order builder of a spare table (nothing is saved or sent)
  await openTable(w, SPARE);
  const newOrder = w.locator('button', { hasText: /NEW ORDER/ });
  if (await newOrder.count()) {
    await newOrder.click();
    await w.locator('#menu-search').waitFor();
    // Chromium's offline emulation keeps an open WebSocket alive, so also drop the hub socket (proxied below);
    // SignalR then reconnects against an offline network, as on a phone that loses Wi-Fi.
    await waiterCtx.setOffline(true);
    for (const ws of hubSockets.splice(0)) await ws.close({ code: 4000, reason: 'offline drill' }).catch(() => {});
    const banner = w.locator('.banner.warning', { hasText: 'Offline' });
    const bannerShown = await banner.waitFor({ timeout: 20000 }).then(() => true, () => false);
    if (!bannerShown) {
      // the pill says Reconnecting but the page does not re-render on a status change (README issue); tap a chip
      log('offline banner did not appear within 20 s; tapping the selected category');
      await shot(w, '24a-waiter-offline-no-banner-yet', { wait: 200 });
      await nudge(w);
      await banner.waitFor({ timeout: 5000 }).catch(() => log('banner still missing after a tap'));
    }
    await shot(w, '24-waiter-offline-banner');
    // a natural error state: open My orders while offline
    await nav(w, 'My orders');
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
  await browser.close();
}
