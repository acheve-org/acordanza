// Drives the Acordanza web app (React frontend + ASP.NET Core API) through
// its golden path: login -> add route -> log tick -> edit -> filter -> delete.
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
  await page.waitForSelector('text=Un registro de vías compartido', { timeout: 15000 });
  await shot('login-screen');

  await page.fill('input[type="text"]', USERNAME);
  await page.fill('input[type="password"]', PASSWORD);
  await page.click('button:has-text("Entrar")');
  await page.waitForSelector(`text=Sesión iniciada como ${USERNAME}`, { timeout: 15000 });
  await shot('logged-in');

  // Add a route (its own page now, at /routes/new)
  await page.click('button:has-text("+ Añadir vía")');
  await page.waitForSelector('text=Añadir una vía');
  const addForm = page.locator('.settings-section');
  await addForm.locator('label:has-text("Nombre") input').fill('Sunny Crimper');
  await addForm.locator('label:has-text("Sector / Zona") input').fill('Riverside Wall');
  await addForm.locator('label:has-text("Grado") input').fill('5.10a');
  await addForm.locator('label:has-text("Descripción") textarea').fill('Fun crimpy face climbing.');
  await addForm.locator('label:has-text("Etiquetas") input').fill('crimpy, classic');
  await addForm.locator('label:has-text("Primera ascensión") input').fill('Jane Doe, 1998');
  await addForm.locator('button:has-text("Guardar vía")').click();
  await page.waitForSelector('text=Sunny Crimper', { timeout: 10000 });
  await shot('route-added');

  // Open detail, log a tick
  await page.click('text=Sunny Crimper');
  await page.waitForSelector('text=Registrar un ascenso');
  const detailModal = page.locator('.modal').last();
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

  // Edit the route (its own page now, at /routes/:id/edit)
  await page.click('button:has-text("Editar")');
  await page.waitForSelector('text=Editar vía');
  const editForm = page.locator('.settings-section');
  await editForm.locator('label:has-text("Nombre") input').fill('Sunny Crimper (Updated)');
  await editForm.locator('button:has-text("Guardar vía")').click();
  await page.waitForSelector('text=Sunny Crimper (Updated)', { timeout: 10000 });
  await shot('route-edited');

  // Filters
  const nameFilter = 'input[placeholder="Nombre de la vía o descripción…"]';
  await page.fill(nameFilter, 'Sunny');
  await page.waitForTimeout(500);
  assert.equal(await page.locator('.route-card').count(), 1, 'name filter should match the route');

  await page.fill(nameFilter, 'nonexistentxyz');
  await page.waitForSelector('text=No hay vías que coincidan', { timeout: 5000 });
  await shot('filter-empty');

  await page.fill(nameFilter, '');
  await page.waitForSelector('text=Sunny Crimper (Updated)', { timeout: 5000 });

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
