// Re-runs only the demo steps that failed before the 2026-10-19 fixes (main f382716): held draft from "Save draft",
// takeaway order details, and the real item pictures in the kitchen. Overwrites the matching PNGs in OUT_DIR and prints
// a Pass/Fail line per step. Same set-up as demo-web-flow.mjs (Playwright in the cwd, installed Edge, STATE_DIR identity).
import { createRequire } from 'node:module';
import path from 'node:path';
import fs from 'node:fs';
import os from 'node:os';
import { fileURLToPath } from 'node:url';

const require = createRequire(path.join(process.cwd(), 'noop.js'));
const { chromium } = require('playwright');
const here = path.dirname(fileURLToPath(import.meta.url));
const BASE = process.env.HOTELPOS_URL ?? 'http://localhost:5000/app/';
const OUT = process.env.OUT_DIR ?? path.resolve(here, '..', '..', 'demo');
const STATE_DIR = process.env.STATE_DIR ?? path.join(os.tmpdir(), 'hotelpos-web-flow');
const stateFor = r => (fs.existsSync(path.join(STATE_DIR, r + '.json')) ? path.join(STATE_DIR, r + '.json') : undefined);
const pause = (p, ms = 700) => p.waitForTimeout(ms);
const results = [];
async function step(name, file, page, fn) {
  let res = 'Pass', note = '';
  try { const r = await fn(); if (typeof r === 'string') note = r; } catch (e) { res = 'Fail'; note = String(e.message).split('\n')[0].slice(0, 200); }
  await pause(page, 800);
  await page.screenshot({ path: path.join(OUT, file) });
  results.push({ name, file, res, note });
  console.log(res, name, file, note);
}
async function clearOverlays(page) {
  for (const t of await page.locator('.toasts .toast').all()) await t.click().catch(() => {});
  await pause(page, 200);
}
async function signIn(page, role, user) {
  await page.goto(BASE + 'signin', { waitUntil: 'domcontentloaded' });
  await page.locator('.signin .card').waitFor();
  await page.locator('.segmented .segment', { hasText: role }).click();
  await page.locator('input[autocomplete=username]').fill(user);
  await page.locator('input[type=password]').fill('Pass@123');
  await page.locator('button[type=submit]').click();
}
const browser = await chromium.launch({ channel: process.env.PW_CHANNEL ?? 'msedge', headless: true });
const common = { serviceWorkers: 'block', deviceScaleFactor: 2, hasTouch: true, colorScheme: 'light', locale: 'en-IN', timezoneId: 'Asia/Kolkata' };
const wc = await browser.newContext({ ...common, viewport: { width: 390, height: 844 }, isMobile: true, storageState: stateFor('waiter') });
const kc = await browser.newContext({ ...common, viewport: { width: 1024, height: 768 }, storageState: stateFor('kitchen') });
const w = await wc.newPage(); const k = await kc.newPage();
w.setDefaultTimeout(15000); k.setDefaultTimeout(15000);
try {
  await signIn(w, 'Waiter', 'waiter1');
  await w.waitForURL(/\/home|\/tables/);
  await pause(w, 1500);
  if (process.env.SKIP_DRAFT !== '1') await step('Save a draft from a new order', '02-waiter-phone/22-draft-saved.png', w, async () => {
    await clearOverlays(w);
    await w.locator('nav.bottomnav button.tabbtn', { hasText: 'New order' }).click();
    await w.locator('.overlay .sheet').waitFor();
    await w.locator('[data-testid=picker-takeaway]').click();
    await w.locator('#menu-search').waitFor();
    await pause(w, 700);
    if (await w.locator('.banner.info', { hasText: 'restored' }).count()) {
      await w.locator('[data-testid=cart-review]').click();
      for (let i = 0; i < 40 && await w.locator('.cart-line').count(); i++) { await w.locator('.cart-line [data-testid=cart-line-delete]').first().click(); await pause(w, 150); }
      await w.locator('.overlay .sheet .sheet-head button[aria-label="Close"]').first().click();
      await pause(w, 400);
    }
    await w.locator('#menu-search').fill('Masala Chai');
    await pause(w, 500);
    await w.locator('.item-wrap', { has: w.locator('text="Masala Chai"') }).locator('.item-card:not([disabled])').first().click();
    await pause(w, 500);
    if (await w.locator('.overlay .sheet').count()) {
      for (const g of await w.locator('.sheet .options:not(.quick-notes)').all()) await g.locator('button:not([disabled])').first().click().catch(() => {});
      await w.locator('[data-testid=options-confirm]').click();
      await pause(w, 500);
    }
    await clearOverlays(w);
    await w.locator('[data-testid=cart-review]').click();
    await w.locator('.overlay .sheet .cart-line').first().waitFor();
    await w.locator('[data-testid=save-draft]').click();
    await w.waitForURL(/\/my-orders/, { timeout: 10000 });
    const toast = (await w.locator('.toasts .toast').first().innerText({ timeout: 3000 }).catch(() => '')).trim();
    if (!/held/i.test(toast)) throw new Error('toast is "' + toast + '"');
    return `toast: "${toast}"`;
  });
  fs.rmSync(path.join(OUT, '02-waiter-phone/23-held-tab-empty.png'), { force: true });
  if (process.env.SKIP_DRAFT !== '1') await step('Held tab lists the saved draft', '02-waiter-phone/23-held-tab.png', w, async () => {
    await clearOverlays(w);
    await w.locator('.tabs .tab', { hasText: 'Held' }).click();
    await w.locator('[data-testid=held-order-card]').first().waitFor({ timeout: 8000 });
    await w.locator('[data-testid=resume-order]').first().waitFor();
    return `${await w.locator('[data-testid=held-order-card]').count()} held order(s)`;
  });
  await step('Takeaway order details with tickets and Print KOT', '02-waiter-phone/28-takeaway-details.png', w, async () => {
    await w.locator('nav.bottomnav a', { hasText: 'Orders' }).click().catch(() => {});
    await w.locator('.tabs .tab', { hasText: 'Active' }).click();
    await pause(w, 600);
    // a sent takeaway order (drafts also appear in the Active list, so skip those)
    const card = w.locator('[data-testid=order-card]:not([data-status=Draft])', { hasText: /TA-\d+/ }).first();
    await card.waitFor();
    const label = (await card.innerText()).match(/TA-\d+/)[0];
    await card.click();
    await w.waitForURL(/\/orders\/\d+$/);
    await w.locator('[data-testid=order-details]').first().waitFor({ timeout: 10000 });
    await w.locator('[data-testid=order-tickets]').waitFor({ timeout: 10000 });
    await w.locator('[data-testid=print-kot]').first().waitFor();
    await w.locator('[data-testid=order-tickets]').scrollIntoViewIfNeeded();
    if (!/TA-\d+/.test(await w.locator('body').innerText())) throw new Error('no TA label');
    return `order ${label}, ${await w.locator('[data-testid=order-ticket]').count()} ticket(s)`;
  });

  // ---- kitchen ----
  await signIn(k, 'Kitchen', 'kitchen1');
  await k.waitForURL(/\/kitchen/);
  await pause(k, 2000);
  if (k.url().includes('/kitchen/station')) { await k.locator('.card button', { hasText: 'All stations' }).click(); await k.waitForURL(/\/kitchen$/); }
  const withPic = k.locator('article.ticket', { has: k.locator('.ticket-thumb') });
  await step('Ticket detail pane with real item pictures', '03-kitchen-tablet/04-ticket-detail-pictures.png', k, async () => {
    // the Show item pictures preference is per device; make sure it is on so a ticket with pictures can be found, then use the pane
    // the newest ticket still in the New column (older dev-DB tickets are flagged Delayed)
    const tickets = k.locator('article.ticket[data-status=New]', { hasText: /Gulab Jamun|Chicken Biryani|Masala Chai|Butter Naan/ });
    await tickets.first().waitFor();
    await tickets.last().scrollIntoViewIfNeeded();
    await tickets.last().locator('.ticket-top').click();
    await k.locator('[data-testid=ticket-detail]').waitFor();
    await k.locator('.kds-detail .ticket-thumb').first().waitFor({ state: 'attached', timeout: 8000 });
    await pause(k, 1000);
    const loaded = await k.locator('.kds-detail .ticket-thumb').evaluateAll(els => els.filter(e => e.complete && e.naturalWidth > 0).length);
    if (!loaded) throw new Error('no picture loaded: ' + await k.locator('.kds-detail .ticket-thumb').first().getAttribute('src'));
    return `${loaded} picture(s) loaded in the pane`;
  });
  fs.rmSync(path.join(OUT, '03-kitchen-tablet/05-ticket-detail-pictures-workaround.png'), { force: true });
  await step('Board with thumbnails on the cards', '03-kitchen-tablet/08-board-thumbnails.png', k, async () => {
    await k.locator('.detail-pane button[aria-label="Close"]').click();
    await k.locator('.detail-overlay').waitFor({ state: 'detached' });
    await clearOverlays(k);
    await k.locator('nav.headnav a[aria-label="Station"]').click();
    const cb = k.locator('[data-testid=show-pictures]');
    await cb.waitFor();
    if (!(await cb.isChecked())) await cb.check();
    await pause(k, 600);
    await k.locator('nav.headnav a[aria-label="Kitchen"]').click();
    await k.locator('article.ticket .ticket-thumb').first().waitFor({ state: 'attached', timeout: 10000 });
    await pause(k, 1000);
    const loaded = await k.locator('article.ticket .ticket-thumb').evaluateAll(els => els.filter(e => e.complete && e.naturalWidth > 0).length);
    if (!loaded) throw new Error('no thumbnail loaded');
    await k.locator('article.ticket', { has: k.locator('.ticket-thumb') }).last().scrollIntoViewIfNeeded();
    return `${loaded} thumbnail(s) loaded on the board`;
  });
  fs.rmSync(path.join(OUT, '03-kitchen-tablet/09-board-thumbnails-workaround.png'), { force: true });
} finally {
  await browser.close();
  console.log(JSON.stringify(results.map(r => [r.name, r.res])));
}
