// Drives the Acordanza web app (React frontend + ASP.NET Core API) through
// its golden path: login -> home (the four sections) -> add route (area tree,
// orientation, gear, opener) -> log tick -> edit -> the aperturista's page under
// "Sigue" -> filter (name, area, gear) -> delete.
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

// Areas/openers have no delete endpoint (by design — no admin page), so repeated runs leave
// residual rows behind. Suffix names with a run id so a stale "Riverside Wall" from a previous
// run never collides with (or gets picked instead of) this run's.
const RUN_ID = Date.now();
const AREA_NAME = `Riverside Wall ${RUN_ID}`;
const SUBAREA_NAME = `Sector Norte ${RUN_ID}`;
const OPENER_NAME = `Jane Doe ${RUN_ID}`;
// The route is deleted at the end of a successful run, but a failed run leaves it behind —
// so it carries the run id too, keeping the "exactly one match" assertions below honest.
const ROUTE_NAME = `Sunny Crimper ${RUN_ID}`;
const EDITED_ROUTE_NAME = `${ROUTE_NAME} (Updated)`;
const TICK_NOTES = 'Encadenada al segundo intento, paso clave la regleta';

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
  // Login lands on the home page — the hub over the app's four sections (issue #108).
  await page.waitForSelector('.home-card', { timeout: 15000 });
  assert.equal(await page.locator('.home-card').count(), 4, 'home page should show the four sections');
  assert.deepEqual(
    await page.locator('.home-card h2').allInnerTexts(),
    ['Busca', 'Explora', 'Sigue', 'Mi acordanza'],
    'home cards should match the four nav sections'
  );
  await shot('home');

  // Explora: the map plus the browsable list of zones.
  await page.click('.main-nav-item:has-text("Explora")');
  await page.waitForSelector('.area-map', { timeout: 10000 });
  await page.waitForSelector('.settings-section h2:has-text("Zonas")');
  await shot('explore');

  // Busca: the routes list with its filters.
  await page.click('.main-nav-item:has-text("Busca")');
  await page.waitForSelector('button:has-text("+ Añadir vía")', { timeout: 10000 });
  await shot('search');

  // Add a route (its own page now, at /routes/new)
  await page.click('button:has-text("+ Añadir vía")');
  await page.waitForSelector('text=Añadir una vía');
  const addForm = page.locator('.settings-section');
  await addForm.locator('label:has-text("Nombre") input').fill(ROUTE_NAME);
  await addForm.locator('label:has-text("Grado") input').fill('5.10a');

  // Sector: create a top-level area, then a sub-sector under it, via the inline area-tree picker.
  const sectorField = addForm.locator('label:has-text("Sector")');
  await sectorField.locator('button:has-text("+ Añadir nuevo sector")').click();
  await sectorField.locator('input[placeholder="Nombre del nuevo sector"]').fill(AREA_NAME);
  await sectorField.locator('.combo-create-form button:has-text("Añadir")').click();
  await page.waitForTimeout(300);
  await sectorField.locator('button:has-text("+ Añadir nuevo sector")').click();
  await sectorField.locator('input[placeholder="Nombre del nuevo sector"]').fill(SUBAREA_NAME);
  await sectorField.locator('.combo-create-form select').selectOption({ label: AREA_NAME });
  await sectorField.locator('.combo-create-form button:has-text("Añadir")').click();
  await page.waitForTimeout(300);
  const areaBreadcrumb = `${AREA_NAME} › ${SUBAREA_NAME}`;
  assert.equal(
    await sectorField.locator('input').first().inputValue(),
    areaBreadcrumb,
    'sector picker should show the full area breadcrumb after creating a sub-sector'
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
  await page.waitForSelector(`text=${ROUTE_NAME}`, { timeout: 10000 });
  assert.equal(
    await page.locator('.rc-sub').first().innerText(),
    areaBreadcrumb,
    'route card should show the full area breadcrumb'
  );
  // .rc-type is styled with text-transform: uppercase, so innerText() reflects that rendering.
  assert.equal(await page.locator('.rc-type').first().innerText(), 'EQUIPADA', 'route card should show the gear label');
  await shot('route-added');

  // Open detail, check the fields, log a tick (the tick form is its own modal on top)
  await page.click(`text=${ROUTE_NAME}`);
  await page.waitForSelector('button:has-text("+ Registrar ascenso")');
  const detailModal = page.locator('.modal').last();
  assert.ok((await detailModal.locator('.detail-meta').innerText()).includes(areaBreadcrumb));
  assert.match(await detailModal.locator('.detail-fa').first().innerText(), /Este.*Equipada/);
  assert.ok((await detailModal.locator('text=Apertura:').innerText()).includes(OPENER_NAME));
  await shot('route-detail');

  await detailModal.locator('button:has-text("+ Registrar ascenso")').click();
  const tickModal = page.locator('.modal').last();
  await tickModal.locator('label:has-text("Fecha") input').fill('2026-07-15');
  await tickModal.locator('label:has-text("Puntuación") input').fill('5');
  await tickModal.locator('label:has-text("Notas") input').fill(TICK_NOTES);
  await tickModal.locator('button:has-text("Registrar")').click();
  // Ticks are private, so the detail modal never lists them — the route card just gets a check.
  await page.waitForSelector('.rc-tick-icon', { timeout: 10000 });
  await shot('tick-logged');

  // Edit the route (its own page now, at /routes/:id/edit) — confirm fields pre-fill correctly
  await page.click('button:has-text("Editar")');
  await page.waitForSelector('text=Editar vía');
  const editForm = page.locator('.settings-section');
  // The picker only fills in once its own areas fetch lands, so wait rather than read straight away.
  const sectorInput = editForm.locator('label:has-text("Sector") input').first();
  await sectorInput.waitFor();
  await page
    .waitForFunction(
      (expected) =>
        [...document.querySelectorAll('.picker-stack input')].some((i) => i.value === expected),
      areaBreadcrumb,
      { timeout: 10000 }
    )
    .catch(() => {});
  assert.equal(
    await sectorInput.inputValue(),
    areaBreadcrumb,
    'edit form should preselect the saved sub-sector'
  );
  assert.equal(await editForm.locator('label:has-text("Orientación") select').inputValue(), 'east');
  assert.equal(await editForm.locator('label:has-text("Equipamiento") select').inputValue(), 'bolted');
  assert.match(await editForm.locator('label:has-text("Apertura") .tag-chip').innerText(), new RegExp(OPENER_NAME));
  await editForm.locator('label:has-text("Nombre") input').fill(EDITED_ROUTE_NAME);
  await editForm.locator('button:has-text("Guardar vía")').click();
  await page.waitForSelector(`text=${EDITED_ROUTE_NAME}`, { timeout: 10000 });
  await shot('route-edited');

  // Mi acordanza: the tick shows up in the (private) history, and is deleted from there.
  await page.click('.main-nav-item:has-text("Mi acordanza")');
  await page.click('.side-nav-item:has-text("Mis ascensiones")');
  await page.waitForSelector('h1:has-text("Mis vías")', { timeout: 10000 });
  await page.fill('input[placeholder="Nombre de la vía o descripción…"]', String(RUN_ID));
  await page.waitForTimeout(600);
  assert.equal(await page.locator('.route-card').count(), 1, 'the tick should show up in Mis ascensiones');
  assert.ok((await page.locator('.trc-notes').innerText()).includes(TICK_NOTES));
  await shot('my-ticks');

  await page.click('button[aria-label="Eliminar registro"]');
  await page.locator('.modal').last().locator('button:has-text("Eliminar")').click();
  await page.waitForTimeout(600);
  assert.equal(await page.locator('.route-card').count(), 0, 'tick should be removed');

  // Sigue: the opener created above now has a card, and their page lists the route.
  await page.click('.main-nav-item:has-text("Sigue")');
  await page.waitForSelector('.fa-page h1:has-text("Sigue")', { timeout: 10000 });
  await page.fill('input[placeholder="Nombre del aperturista…"]', OPENER_NAME);
  await page.waitForTimeout(500);
  assert.equal(await page.locator('.fa-card').count(), 1, 'searching by name should match the new opener');
  assert.equal(await page.locator('.fa-card-count').innerText(), '1 vía abierta');
  await shot('first-ascensionists');

  await page.click('.fa-card');
  await page.waitForSelector(`h1:has-text("${OPENER_NAME}")`, { timeout: 10000 });
  await page.waitForSelector('.route-card', { timeout: 10000 });
  assert.equal(
    await page.locator('.route-card').count(),
    1,
    "the opener's page should list the route they opened"
  );
  await shot('first-ascensionist-detail');

  await page.click('.main-nav-item:has-text("Busca")');
  await page.waitForSelector('button:has-text("+ Añadir vía")', { timeout: 10000 });

  // Filters: name, then area (picking the top-level area should still surface a route filed
  // under its sub-sector), then gear, then reset.
  const nameFilter = 'input[placeholder="Nombre de la vía o descripción…"]';
  await page.fill(nameFilter, String(RUN_ID));
  await page.waitForTimeout(500);
  assert.equal(await page.locator('.route-card').count(), 1, 'name filter should match the route');

  await page.fill(nameFilter, 'nonexistentxyz');
  await page.waitForSelector('text=No hay vías que coincidan', { timeout: 5000 });
  await shot('filter-empty');
  await page.fill(nameFilter, '');
  await page.waitForSelector(`text=${EDITED_ROUTE_NAME}`, { timeout: 5000 });

  const filters = page.locator('.filters');
  await filters.locator('.combobox input').click();
  await filters.locator(`.combobox-option:text-is("${AREA_NAME}")`).click();
  await page.waitForTimeout(500);
  assert.equal(
    await page.locator('.route-card').count(),
    1,
    'filtering by the parent area should include routes filed under its sub-sector'
  );
  await shot('filter-by-area');

  await filters.locator('label:has-text("Equipamiento") + select').selectOption({ label: 'Equipada' });
  await page.waitForTimeout(500);
  assert.equal(await page.locator('.route-card').count(), 1, 'gear filter should match the route');

  await filters.locator('label:has-text("Grado") + select').selectOption('5.10a');
  await filters.locator('label:has-text("Orientación") + select').selectOption('east');
  await page.waitForTimeout(500);
  assert.equal(await page.locator('.route-card').count(), 1, 'grade + orientation filters should match the route');
  await filters.locator('label:has-text("Orientación") + select').selectOption('north');
  await page.waitForSelector('text=No hay vías que coincidan', { timeout: 5000 });
  await filters.locator('label:has-text("Grado") + select').selectOption('');
  await filters.locator('label:has-text("Orientación") + select').selectOption('');

  await filters.locator('.combobox input').click();
  await filters.locator('.combobox-option:text-is("Cualquier sector")').click();
  await filters.locator('label:has-text("Equipamiento") + select').selectOption('');
  await page.waitForTimeout(500);

  // Delete the route (cleans up test data). Filtered down to this run's route first, so the
  // "nothing left" assertion isn't thrown off by whatever else the database already holds.
  await page.fill(nameFilter, String(RUN_ID));
  await page.waitForTimeout(500);
  await page.click(`text=${EDITED_ROUTE_NAME}`);
  await page.waitForSelector('button:has-text("+ Registrar ascenso")');
  await page.locator('.modal').last().locator('button:has-text("Eliminar")').click();
  // Confirmation is its own modal on top of the detail one.
  await page.locator('.modal').last().locator('button:has-text("Eliminar")').click();
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
