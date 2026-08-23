/// <reference types="node" />
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { orientationLabels, gearLabels } from './types';

// Read straight from the source of truth (backend/Acordanza.Api/Services/RouteWrite.cs's
// AllowedOrientations/AllowedGear) instead of duplicating the literals here, so a backend addition
// that frontend/CLAUDE.md's "keep in sync by hand" note gets missed fails this test instead of
// silently rendering `undefined` in the UI.
const routeWritePath = path.resolve(
  import.meta.dirname,
  '../../../backend/Acordanza.Api/Services/RouteWrite.cs',
);
const routeWriteSource = readFileSync(routeWritePath, 'utf-8');

function extractAllowedSet(fieldName: string): string[] {
  const match = routeWriteSource.match(new RegExp(`${fieldName}\\s*=\\s*\\[([^\\]]*)\\]`));
  if (!match) throw new Error(`Could not find ${fieldName} in RouteWrite.cs`);
  return match[1]!.split(',').map((s) => s.trim().replace(/^"|"$/g, '')).filter(Boolean);
}

describe('orientationLabels', () => {
  it('covers exactly the backend RouteWrite.AllowedOrientations set', () => {
    const backendAllowedOrientations = extractAllowedSet('AllowedOrientations');

    expect(Object.keys(orientationLabels).sort()).toEqual([...backendAllowedOrientations].sort());
  });
});

describe('gearLabels', () => {
  it('covers exactly the backend RouteWrite.AllowedGear set', () => {
    const backendAllowedGear = extractAllowedSet('AllowedGear');

    expect(Object.keys(gearLabels).sort()).toEqual([...backendAllowedGear].sort());
  });
});
