// Drives the Acordanza web app (React frontend + ASP.NET Core API) through
// its golden path: login -> add route (crag tree, orientation, gear, opener)
// -> log tick -> edit -> filter (name, crag, gear) -> delete.
// Requires both dev servers already running (see SKILL.md).
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';

const HERE = dirname(fileURLToPath(import.meta.url));
const SHOTS_DIR = join(HERE, 'shots');
mkdirSync(SHOTS_DIR, { recursive: true });

const FRONTEND_URL = process.env.ACORDANZA_FRONTEND_URL ?? 'http://localhost:5173';
const USERNAME = process.env.ACORDANZA_USER ?? 'hugo@example.com';
const PASSWORD = process.env.ACORDANZA_PASSWORD ?? 'testpass123';

// Crags/openers have no delete endpoint (by design — no admin page), so repeated runs leave
// residual rows behind. Suffix names with a run id so a stale "Riverside Wall" from a previous
// run never collides with (or gets picked instead of) this run's.
const RUN_ID = Date.now();
const CRAG_NAME = `Riverside Wall ${RUN_ID}`;
const SUBCRAG_NAME = `Sector Norte ${RUN_ID}`;
const OPENER_NAME = `Jane Doe ${RUN_ID}`;

const consoleErrors = [];
const browser = await chromium.launch();
const page = await browser.newPage();
page.on('console', (msg) => {
  if (msg.type() === 'error') consoleErrors.push(msg.text());
});
page.on('pageerror', (err) => consoleErrors.push(`pageerror: ${err.message}`));

let shotIndex = 0;
async function shot(name) {
  shotIndex += 1;
  const file = `${String(shotIndex).padStart(2, '0')}-${name}.png`;
  await page.screenshot({ path: join(SHOTS_DIR, file), fullPage: true });
  console.log(`screenshot: ${file}`);
}

try {
  await page.goto(FRONTEND_URL);
  await page.waitForSelector('text=Encuentra la ruta que quieres escalar', { timeout: 15000 });
  await shot('login-screen');

  await page.fill('input[type="email"]', USERNAME);
  await page.fill('input[type="password"]', PASSWORD);
  await page.click('button:has-text("Entrar")');
  await page.waitForSelector('button:has-text("+ Añadir vía")', { timeout: 15000 });
  await shot('logged-in');

  // Add a route (its own page now, at /routes/new)
  await page.click('button:has-text("+ Añadir vía")');
  await page.waitForSelector('text=Añadir una vía');
  const addForm = page.locator('.settings-section');
  await addForm.locator('label:has-text("Nombre") input').fill('Sunny Crimper');
  await addForm.locator('label:has-text("Grado") input').fill('5.10a');

  // Sector: create a top-level crag, then a sub-sector under it, via the inline crag-tree picker.
  const sectorField = addForm.locator('label:has-text("Sector")');
  await sectorField.locator('button:has-text("+ Añadir nuevo sector")').click();
  await sectorField.locator('input[placeholder="Nombre del nuevo sector"]').fill(CRAG_NAME);
  await sectorField.locator('.combo-create-form button:has-text("Añadir")').click();
  await page.waitForTimeout(300);
  await sectorField.locator('button:has-text("+ Añadir nuevo sector")').click();
  await sectorField.locator('input[placeholder="Nombre del nuevo sector"]').fill(SUBCRAG_NAME);
  await sectorField.locator('.combo-create-form select').selectOption({ label: CRAG_NAME });
  await sectorField.locator('.combo-create-form button:has-text("Añadir")').click();
  await page.waitForTimeout(300);
  const cragBreadcrumb = `${CRAG_NAME} › ${SUBCRAG_NAME}`;
  assert.equal(
    await sectorField.locator('input').first().inputValue(),
    cragBreadcrumb,
    'sector picker should show the full crag breadcrumb after creating a sub-sector'
  );

  await addForm.locator('label:has-text("Orientación") select').selectOption({ label: 'Este' });
  await addForm.locator('label:has-text("Equipamiento") select').selectOption({ label: 'Equipada' });
  await addForm.locator('label:has-text("Descripción") textarea').fill('Fun crimpy face climbing.');
  await addForm.locator('label:has-text("Etiquetas") input').fill('crimpy, classic');

  const openerField = addForm.locator('label:has-text("Apertura")');
  await openerField.locator('button:has-text("+ Nuevo aperturista")').click();
  await openerField.locator('input[placeholder="Nombre del aperturista"]').fill(OPENER_NAME);
  await openerField.locator('.combo-create-form button:has-text("Añadir")').click();
  await page.waitForTimeout(300);
  await shot('route-form-filled');

  await addForm.locator('button:has-text("Guardar vía")').click();
  await page.waitForSelector('text=Sunny Crimper', { timeout: 10000 });
  assert.equal(
    await page.locator('.rc-sub').first().innerText(),
    cragBreadcrumb,
    'route card should show the full crag breadcrumb'
  );
  // .rc-type is styled with text-transform: uppercase, so innerText() reflects that rendering.
  assert.equal(await page.locator('.rc-type').first().innerText(), 'EQUIPADA', 'route card should show the gear label');
  await shot('route-added');

  // Open detail, check the new fields, log a tick
  await page.click('text=Sunny Crimper');
  await page.waitForSelector('text=Registrar un ascenso');
  const detailModal = page.locator('.modal').last();
  assert.ok((await detailModal.locator('.detail-meta').innerText()).includes(cragBreadcrumb));
  assert.match(await detailModal.locator('.detail-fa').first().innerText(), /Este.*Equipada/);
  assert.ok((await detailModal.locator('text=Apertura:').innerText()).includes(OPENER_NAME));
  await shot('route-detail');

  await detailModal.locator('input[type="date"]').fill('2026-07-15');
  await detailModal.locator('input[type="number"]').fill('5');
  await detailModal.locator('label:has-text("Notas") input').fill('Encadenada al segundo intento, paso clave la regleta');
  await detailModal.locator('button:has-text("Registrar")').click();
  await page.waitForSelector('text=Encadenada al segundo intento', { timeout: 10000 });
  await shot('tick-logged');

  // Delete the tick
  await page.click('.tick-remove');
  await page.waitForTimeout(500);
  assert.equal(await page.locator('.tick-row').count(), 0, 'tick should be removed');

  // Edit the route (its own page now, at /routes/:id/edit) — confirm fields pre-fill correctly
  await page.click('button:has-text("Editar")');
  await page.waitForSelector('text=Editar vía');
  const editForm = page.locator('.settings-section');
  assert.equal(
    await editForm.locator('label:has-text("Sector") input').first().inputValue(),
    cragBreadcrumb,
    'edit form should preselect the saved sub-sector'
  );
  assert.equal(await editForm.locator('label:has-text("Orientación") select').inputValue(), 'east');
  assert.equal(await editForm.locator('label:has-text("Equipamiento") select').inputValue(), 'bolted');
  assert.match(await editForm.locator('label:has-text("Apertura") .tag-chip').innerText(), new RegExp(OPENER_NAME));
  await editForm.locator('label:has-text("Nombre") input').fill('Sunny Crimper (Updated)');
  await editForm.locator('button:has-text("Guardar vía")').click();
  await page.waitForSelector('text=Sunny Crimper (Updated)', { timeout: 10000 });
  await shot('route-edited');

  // Filters: name, then crag (picking the top-level crag should still surface a route filed
  // under its sub-sector), then gear, then reset.
  const nameFilter = 'input[placeholder="Nombre de la vía o descripción…"]';
  await page.fill(nameFilter, 'Sunny');
  await page.waitForTimeout(500);
  assert.equal(await page.locator('.route-card').count(), 1, 'name filter should match the route');

  await page.fill(nameFilter, 'nonexistentxyz');
  await page.waitForSelector('text=No hay vías que coincidan', { timeout: 5000 });
  await shot('filter-empty');
  await page.fill(nameFilter, '');
  await page.waitForSelector('text=Sunny Crimper (Updated)', { timeout: 5000 });

  const filters = page.locator('.filters');
  await filters.locator('label:has-text("Sector") + .combo input').click();
  await filters.locator(`.combo-option:text-is("${CRAG_NAME}")`).click();
  await page.waitForTimeout(500);
  assert.equal(
    await page.locator('.route-card').count(),
    1,
    'filtering by the parent crag should include routes filed under its sub-sector'
  );
  await shot('filter-by-crag');

  await filters.locator('label:has-text("Equipamiento") + select').selectOption({ label: 'Equipada' });
  await page.waitForTimeout(500);
  assert.equal(await page.locator('.route-card').count(), 1, 'gear filter should match the route');

  await filters.locator('.combo input').click();
  await filters.locator('.combo-option:text-is("Cualquier sector")').click();
  await filters.locator('label:has-text("Equipamiento") + select').selectOption('');
  await page.waitForTimeout(500);

  // Delete the route (cleans up test data)
  await page.click('text=Sunny Crimper (Updated)');
  await page.waitForSelector('text=Registrar un ascenso');
  page.once('dialog', (d) => d.accept());
  await page.click('button:has-text("Eliminar")');
  await page.waitForTimeout(1000);
  assert.equal(await page.locator('.route-card').count(), 0, 'route should be deleted');
  await shot('route-deleted');

  assert.deepEqual(consoleErrors, [], `console errors: ${JSON.stringify(consoleErrors)}`);

  console.log('PASS: golden path completed with no console errors');
} catch (err) {
  console.error('FAIL:', err.message);
  await shot('failure-state');
  console.log('console errors so far:', JSON.stringify(consoleErrors, null, 2));
  process.exitCode = 1;
} finally {
  await browser.close();
}
