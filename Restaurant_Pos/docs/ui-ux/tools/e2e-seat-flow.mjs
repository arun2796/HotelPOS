// HotelPOS end-to-end flow for the client's KOT requirements (stock counts, seats, parcel, table merge, per-seat split bills).
// Waiter (phone) and kitchen (tablet) are driven through the real browser UI; the cashier is the Windows app, so the
// cashier steps (claim, split by seat, finalize, pay) go through the same HTTP API the app uses.
//
// How to run (Playwright is not installed in the repo; use any scratch folder OUTSIDE the repo):
//   1. Start the API in Development mode with a throwaway database and the demo seed (see docs/dev-setup.md);
//      the web client is then served at http://localhost:5000/app/.
//   2. In the scratch folder:  npm i playwright   (browsers are cached by Playwright)
//   3. node <repo>/docs/ui-ux/tools/e2e-seat-flow.mjs
// Env: HOTELPOS_URL (default http://localhost:5000), PW_CHANNEL (msedge | chrome; default: bundled chromium),
//      OUT_DIR (screenshots, default artifacts/e2e/web), E2E_TABLES (primary,companion; default T01,T02).
// Uses demo accounts waiter1 / kitchen1 / cashier1 / manager1 (password Pass@123). It needs the two tables free.
import { createRequire } from 'node:module';
import path from 'node:path';
import fs from 'node:fs';
import os from 'node:os';
import { fileURLToPath } from 'node:url';

const require = createRequire(path.join(process.cwd(), 'noop.js'));
const { chromium } = require('playwright');

const here = path.dirname(fileURLToPath(import.meta.url));
const ORIGIN = process.env.HOTELPOS_URL ?? 'http://localhost:5000';
const BASE = ORIGIN + '/app/';
const CHANNEL = process.env.PW_CHANNEL || undefined;
const OUT = process.env.OUT_DIR ?? path.resolve(here, '..', '..', '..', 'artifacts', 'e2e', 'web');
const [PRIMARY, COMPANION] = (process.env.E2E_TABLES ?? 'T01,T02').split(',');
fs.mkdirSync(OUT, { recursive: true });

const results = [];
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
function check(name, ok, detail = '') {
  results.push({ name, ok: !!ok, detail: String(detail) });
  log(ok ? 'PASS' : 'FAIL', name, ok ? '' : detail);
}
const pause = (p, ms = 500) => p.waitForTimeout(ms);
const shot = async (p, name) => { await pause(p, 600); await p.screenshot({ path: path.join(OUT, name + '.png') }); };

// ---------------- API helper (cashier / setup steps) ----------------
const tokens = {};
async function api(user, method, url, body, extraHeaders = {}) {
  if (!tokens[user]) {
    // Another terminal signing in as the same demo user at the same moment can make the login lose a write race: retry.
    let j;
    for (let attempt = 0; attempt < 5; attempt++) {
      const r = await fetch(ORIGIN + '/api/auth/login', { method: 'POST', headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ username: user, password: 'Pass@123', deviceName: user === 'cashier1' ? 'E2E-COUNTER' : 'E2E-' + user }) });
      j = await r.json();
      if (j.success) break;
      await new Promise(res => setTimeout(res, 700));
    }
    if (!j.success) throw new Error(`login ${user}: ${j.message}`);
    tokens[user] = j.data.accessToken;
  }
  const r = await fetch(ORIGIN + url, { method, headers: { 'content-type': 'application/json', authorization: 'Bearer ' + tokens[user], 'Idempotency-Key': crypto.randomUUID(), ...extraHeaders }, body: body === undefined ? undefined : JSON.stringify(body) });
  const j = await r.json().catch(() => ({}));
  return { status: r.status, ...j };
}

// ---------------- UI helpers ----------------
async function signIn(page, role, user) {
  await page.goto(BASE + 'signin', { waitUntil: 'domcontentloaded' });
  await page.locator('.signin .card').waitFor();
  await page.locator('.segmented .segment', { hasText: role }).click();
  await page.locator('input[autocomplete=username]').fill(user);
  await page.locator('input[type=password]').fill('Pass@123');
  await page.locator('button[type=submit]').click();
  await page.waitForURL(/\/home|\/tables|\/kitchen/);
}
async function clearOverlays(page) {
  for (const t of await page.locator('.toasts .toast').all()) await t.click().catch(() => {});
  await pause(page, 200);
}
async function openTable(page, code) {
  await clearOverlays(page);
  await page.locator('nav.bottomnav a', { hasText: 'Tables' }).first().click();
  await page.locator('.table-card').first().waitFor();
  await page.locator('.table-card', { has: page.locator('.table-code', { hasText: new RegExp(`^${code}$`) }) }).click();
  await page.locator('.detail-card h1').first().waitFor();
  await pause(page, 800);
}
async function action(page, key) { await clearOverlays(page); await page.locator(`.action-list [data-action="${key}"]`).click(); }
async function addDish(page, name) {
  await clearOverlays(page);
  await page.locator('#menu-search').fill(name);
  await pause(page, 500);
  await page.locator('.item-card:not([disabled])').first().click();
  await pause(page, 500);
  await page.locator('#menu-search').fill('');
}

const browser = await chromium.launch({ channel: CHANNEL, headless: true });
const common = { serviceWorkers: 'block', deviceScaleFactor: 2, hasTouch: true, locale: 'en-IN', timezoneId: 'Asia/Kolkata' };
// Every new browser profile registers a terminal and uses a licence seat, so the device identity (localStorage) is kept between runs.
const STATE_DIR = process.env.STATE_DIR ?? path.join(os.tmpdir(), 'hotelpos-e2e-state');
fs.mkdirSync(STATE_DIR, { recursive: true });
const stateFile = role => path.join(STATE_DIR, role + '.json');
const stateFor = role => (fs.existsSync(stateFile(role)) ? stateFile(role) : undefined);
const saveState = async (ctx, role) => { const st = await ctx.storageState(); st.cookies = []; fs.writeFileSync(stateFile(role), JSON.stringify(st)); };
const wctx = await browser.newContext({ ...common, viewport: { width: 390, height: 844 }, isMobile: true, storageState: stateFor('waiter') });
const kctx = await browser.newContext({ ...common, viewport: { width: 1024, height: 768 }, storageState: stateFor('kitchen') });
const w = await wctx.newPage();
const k = await kctx.newPage();
const consoleErrors = [];
for (const [role, p] of [['waiter', w], ['kitchen', k]]) {
  p.setDefaultTimeout(20000);
  p.on('console', m => { if (m.type() === 'error') consoleErrors.push({ role, text: m.text().slice(0, 300) }); });
  p.on('pageerror', e => consoleErrors.push({ role, text: 'pageerror: ' + e.message.slice(0, 300) }));
}

let orderId;
try {
  // Rerunnable: free the two tables from an earlier run (cancel its order, take the companion out, release).
  {
    const tables = (await api('manager1', 'GET', '/api/tables')).data.sections.flatMap(s => s.tables);
    const prim = tables.find(t => t.code === PRIMARY), comp = tables.find(t => t.code === COMPANION);
    if (comp?.mergedIntoTableId) await api('manager1', 'POST', `/api/tables/${comp.id}/unmerge`, {});
    if (prim?.currentOrderId) await api('manager1', 'POST', `/api/orders/${prim.currentOrderId}/cancel`, { reason: 'e2e cleanup' });
    const again = (await api('manager1', 'GET', '/api/tables')).data.sections.flatMap(s => s.tables);
    for (const t of [again.find(x => x.code === PRIMARY), again.find(x => x.code === COMPANION)]) if (t && t.status === 'Occupied') await api('manager1', 'POST', `/api/tables/${t.id}/release`, {});
  }
  // ---- Setup via API: three plain dishes, a packaging charge on one ----
  const menu = (await api('waiter1', 'GET', '/api/menu')).data;
  const plain = menu.items.filter(i => i.isAvailable && i.modifierGroupIds.length === 0 && i.price > 0);
  check('demo menu has three plain dishes', plain.length >= 3, plain.length);
  const [dishA, dishB, dishC] = plain;
  const full = (await api('manager1', 'GET', '/api/menu-items?includeInactive=true')).data.find(i => i.id === dishB.id);
  const upd = await api('manager1', 'PUT', `/api/menu-items/${dishB.id}`, {
    categoryId: full.categoryId, name: full.name, code: full.code, description: full.description, price: full.price, taxId: full.taxId,
    preparationStationId: full.preparationStationId, sortOrder: full.sortOrder, isAvailable: true, isActive: true,
    modifierGroupIds: full.modifierGroupIds, hsnSac: full.hsnSac, packagingCharge: 10, rowVersion: full.rowVersion });
  check('manager sets a packaging charge of 10 on ' + dishB.name, upd.success, upd.message);

  // ---- Kitchen: stock count through the UI ----
  await signIn(k, 'Kitchen', 'kitchen1');
  await k.goto(BASE + 'kitchen/stock', { waitUntil: 'domcontentloaded' });
  await k.locator('[data-testid=stock-list]').first().waitFor();
  const row = k.locator('[data-testid=stock-row]', { hasText: dishA.name }).first();
  await row.locator('[data-testid=stock-input]').fill('10');
  await row.locator('[data-testid=stock-set]').click();
  await k.locator('[data-testid=stock-row]', { hasText: dishA.name }).first().locator('[data-testid=stock-status]', { hasText: '10 left' }).waitFor();
  check(`kitchen sets ${dishA.name} to 10 in the stock page`, true);
  await shot(k, '01-kitchen-stock-set');

  // ---- Waiter: occupy, merge, seat-by-seat parcel order ----
  await signIn(w, 'Waiter', 'waiter1');
  await openTable(w, PRIMARY);
  if (await w.locator('[data-action=occupy]').count()) {
    // guests cannot exceed the table's own capacity (4): the party grows past it by merging a second table
    for (let i = 0; i < 8 && await w.locator('.stepper button[aria-label$="one more"]').first().isEnabled(); i++) await w.locator('.stepper button[aria-label$="one more"]').first().click();
    await w.locator('[data-action=occupy]').click();
    await w.locator('.action-list').waitFor();
    await pause(w, 1000);
  }
  await action(w, 'merge');
  await w.locator('[data-testid=merge-candidates]').waitFor();
  await w.locator('[data-testid=merge-candidate]', { hasText: COMPANION }).click();
  await shot(w, '02-waiter-merge-picker');
  await w.locator('[data-testid=confirm-merge]').click();
  await w.locator('[data-testid=merged-with]').waitFor();
  check(`waiter merged ${COMPANION} into ${PRIMARY} (chip shows)`, (await w.locator('[data-testid=merged-with]').innerText()).includes(COMPANION));
  const map = (await api('waiter1', 'GET', '/api/tables')).data.sections.flatMap(s => s.tables);
  check('server: companion table is locked as merged', map.find(t => t.code === COMPANION)?.mergedIntoTableCode === PRIMARY);
  await shot(w, '03-waiter-merged');

  await action(w, 'new-order');
  await w.locator('#menu-search').waitFor();
  await w.locator('[data-testid=seat-picker]').waitFor();
  const seats = await w.locator('[data-testid=seat-chip]').evaluateAll(els => els.map(e => e.dataset.seat));
  check('seat picker offers the seats of both merged tables only up to capacity', seats.includes(PRIMARY + 'A') && seats.includes(COMPANION + 'D') && !seats.includes(PRIMARY + 'E'), seats.join(','));
  const pick = s => w.locator(`[data-testid=seat-chip][data-seat="${s}"]`).click();
  await pick(PRIMARY + 'A'); await addDish(w, dishA.name);                  // seat A: stock-counted dish
  await pick(PRIMARY + 'B'); await w.locator('[data-testid=parcel-toggle]').click(); await addDish(w, dishB.name); // seat B: parcel
  await w.locator('[data-testid=parcel-toggle]').click();
  await pick(COMPANION + 'A'); await addDish(w, dishC.name);                // a seat of the merged table
  await pick(PRIMARY + 'A'); await addDish(w, dishA.name);                  // seat A again (second portion)
  await shot(w, '04-waiter-seat-cart');
  await clearOverlays(w);
  await w.locator('[data-testid=cart-review]').click();
  await w.locator('.overlay .sheet .cart-line').first().waitFor();
  const lineTexts = await w.locator('.overlay .sheet .cart-line').allInnerTexts();
  check('cart shows seats and the parcel flag per line', lineTexts.some(t => t.includes(PRIMARY + 'B') && /parcel/i.test(t)) && lineTexts.some(t => t.includes(COMPANION + 'A')), lineTexts.join(' | ').replace(/\n/g, ' '));
  await shot(w, '05-waiter-review-sheet');
  await w.locator('[data-testid=send]').click();
  await w.waitForURL(/\/tables\/\d+$/, { timeout: 15000 });
  await w.locator('.detail-card h1').first().waitFor();
  await w.locator('.tabs .tab', { hasText: 'Items' }).click();
  await pause(w, 800);
  check('order items show Seat and Parcel chips', (await w.locator('[data-testid=line-seat]').count()) >= 3 && (await w.locator('[data-testid=line-parcel]').count()) >= 1);
  await shot(w, '06-waiter-order-items');

  // ---- Server truth for the order ----
  const orders = (await api('manager1', 'GET', '/api/orders?pageSize=50')).data.items;
  const summary = orders.find(o => o.tableCode === PRIMARY && !['Paid', 'Completed', 'Cancelled'].includes(o.status));
  check('server has the order on the primary table', !!summary);
  orderId = summary.id;
  const detail = (await api('manager1', 'GET', `/api/orders/${orderId}`)).data;
  const parcel = detail.items.find(i => i.isParcel);
  check('parcel line carries packaging charge 10 in its price', parcel && parcel.packagingCharge === 10 && Math.abs(parcel.unitPrice - (dishB.price + 10)) < 0.001, JSON.stringify(parcel));
  check('seat codes saved on the lines', detail.items.map(i => i.seatCode).sort().join() === [PRIMARY + 'A', PRIMARY + 'B', COMPANION + 'A'].sort().join() && detail.items.find(i => i.seatCode === PRIMARY + 'A').quantity === 2, detail.items.map(i => i.seatCode + 'x' + i.quantity).join());
  const stock = (await api('waiter1', 'GET', '/api/menu')).data.items.find(i => i.id === dishA.id);
  check('stock counted down by the two portions sent (10 -> 8)', stock.stockRemaining === 8, stock.stockRemaining);

  // ---- Kitchen: KOT shows the seat; stock page shows the new count; prepare the order ----
  await k.goto(BASE + 'kitchen', { waitUntil: 'domcontentloaded' });
  await k.locator('article.ticket').first().waitFor();
  const board = await k.locator('body').innerText();
  check('kitchen tickets show the seat of each dish', board.includes('Seat ' + PRIMARY + 'A') || board.includes(PRIMARY + 'A'), board.slice(0, 200));
  await shot(k, '07-kitchen-board');
  const tickets = (await api('manager1', 'GET', `/api/orders/${orderId}`)).data.tickets;
  for (const t of tickets) {
    await api('kitchen1', 'POST', `/api/kitchen/orders/${t.id}/start`, {});
    await api('kitchen1', 'POST', `/api/kitchen/orders/${t.id}/ready`, {});
  }
  await k.goto(BASE + 'kitchen/stock', { waitUntil: 'domcontentloaded' });
  await k.locator('[data-testid=stock-row]', { hasText: dishA.name }).first().locator('[data-testid=stock-status]', { hasText: '8 left' }).waitFor();
  check('kitchen stock page shows 8 left after the order', true);

  // ---- Waiter: served, request bill; the table tile turns to billing ----
  await w.reload({ waitUntil: 'domcontentloaded' });
  await w.locator('.detail-card h1').first().waitFor();
  await action(w, 'serve');
  await pause(w, 1200);
  await action(w, 'request-bill');
  await pause(w, 1500);
  await shot(w, '08-waiter-bill-requested');
  const bills = (await api('cashier1', 'GET', '/api/billing/pending')).data;
  const bill = bills.find(b => b.orderId === orderId);
  check('cashier queue has the bill for the table', !!bill);

  // ---- Cashier (API): split by seat into 3 bills, finalize and pay each; table frees only after the last ----
  await api('cashier1', 'GET', '/api/shifts/current');
  const cur = await api('cashier1', 'GET', '/api/shifts/current');
  if (!cur.data) await api('cashier1', 'POST', '/api/shifts', {});
  const claim = await api('cashier1', 'POST', `/api/billing/${bill.id}/claim`, {});
  check('cashier claims the bill', claim.success, claim.message);
  let b0 = (await api('cashier1', 'GET', `/api/billing/${bill.id}`)).data;
  const ids = seat => b0.items.filter(i => i.seatCode === seat).map(i => i.id);
  const split = await api('cashier1', 'POST', `/api/billing/${bill.id}/split`, {
    rowVersion: b0.rowVersion,
    parts: [{ billItemIds: ids(PRIMARY + 'B') }, { billItemIds: ids(COMPANION + 'A') }] });
  check('split by seat gives three bills for the table', split.success && split.data.siblingBillIds.length === 3, split.message);
  const partIds = split.data.siblingBillIds;
  const methods = (await api('cashier1', 'GET', '/api/payment-methods')).data;
  const upi = methods.find(m => m.code === 'UPI') ?? methods[1];
  const invoices = [];
  for (let i = 0; i < partIds.length; i++) {
    await api('cashier1', 'POST', `/api/billing/${partIds[i]}/claim`, {});
    const b = (await api('cashier1', 'GET', `/api/billing/${partIds[i]}`)).data;
    const paid = await api('cashier1', 'POST', `/api/billing/${partIds[i]}/payments`, { paymentMethodId: upi.id, amount: b.grandTotal, reference: 'UPI-E2E-' + i, rowVersion: b.rowVersion });
    check(`part ${'ABC'[i]} (${b.tableCode}) paid on its own`, paid.success && paid.data.status === 'Settled', paid.message);
    invoices.push(paid.data?.invoiceNumber);
    const t = (await api('waiter1', 'GET', '/api/tables')).data.sections.flatMap(s => s.tables);
    const stillBilling = t.find(x => x.code === PRIMARY).status === 'Billing';
    check(i < partIds.length - 1 ? `table still on billing after part ${'ABC'[i]}` : 'table free after the last part', i < partIds.length - 1 ? stillBilling : !stillBilling);
  }
  check('three different invoice numbers', new Set(invoices).size === 3, invoices.join(','));
  const after = (await api('waiter1', 'GET', '/api/tables')).data.sections.flatMap(s => s.tables);
  check('both merged tables are free again', after.find(t => t.code === PRIMARY).status === 'Available' && after.find(t => t.code === COMPANION).status === 'Available' && !after.find(t => t.code === COMPANION).mergedIntoTableId);
  await w.goto(BASE + 'tables', { waitUntil: 'domcontentloaded' });
  await w.locator('.table-card').first().waitFor();
  await shot(w, '09-waiter-tables-after-payment');

  // ---- Stock-out: last portion of a counted dish switches it off for the waiter ----
  await api('kitchen1', 'PATCH', `/api/menu-items/${dishC.id}/stock`, { mode: 'Exact', quantity: 1 });
  const free = after.find(t => t.status === 'Available' && t.code !== PRIMARY && t.code !== COMPANION && !t.mergedIntoTableId);
  const o2 = await api('waiter1', 'POST', '/api/orders', { tableId: free.id, guestCount: 2, submit: true, items: [{ menuItemId: dishC.id, quantity: 1 }] });
  check('last portion ordered', o2.success, o2.message);
  const menu2 = (await api('waiter1', 'GET', '/api/menu')).data.items.find(i => i.id === dishC.id);
  check('item at zero is switched off for every waiter', menu2.isAvailable === false && menu2.stockRemaining === 0);
  const o3 = await api('waiter1', 'POST', '/api/orders', { tableId: after.find(t => t.status === 'Available' && t.id !== free.id && t.code !== PRIMARY && t.code !== COMPANION).id, guestCount: 2, submit: true, items: [{ menuItemId: dishC.id, quantity: 1 }] });
  check('ordering the sold-out dish is refused with a clear message', !o3.success && /run out|sold out/i.test(o3.message ?? ''), o3.message);
  await api('waiter1', 'POST', `/api/orders/${o2.data.id}/cancel`, { reason: 'e2e cleanup' });
} catch (e) {
  check('flow completed without an unexpected error', false, e.stack?.split('\n').slice(0, 4).join(' | '));
  for (const [n, p] of [['w', w], ['k', k]]) await p.screenshot({ path: path.join(OUT, `failure-${n}.png`) }).catch(() => {});
} finally {
  await saveState(wctx, 'waiter').catch(() => {}); await saveState(kctx, 'kitchen').catch(() => {});
  fs.writeFileSync(path.join(OUT, 'results.json'), JSON.stringify({ results, consoleErrors }, null, 2));
  await browser.close();
}
const failed = results.filter(r => !r.ok);
console.log(`\n${results.length - failed.length}/${results.length} checks passed; ${consoleErrors.length} browser console errors`);
if (consoleErrors.length) console.log(consoleErrors.slice(0, 5));
process.exit(failed.length ? 1 : 0);
