/// <reference types="node" />
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { orientationLabels, gearLabels } from './types';

// Orientation/Gear are now real C# enums (backend/Acordanza.Api/Dtos/RouteEnums.cs,
// DUP-12) rather than hand-maintained string allowlists, so this reads straight from
// their member names instead of duplicating the literals here — a backend addition
// that's missed on the frontend fails this test (orientationLabels) or a `tsc` build
// (gearLabels, whose type now comes straight from the generated OpenAPI schema — see
// types.ts) instead of silently rendering `undefined` in the UI.
const enumsPath = path.resolve(import.meta.dirname, '../../../backend/Acordanza.Api/Dtos/RouteEnums.cs');
const enumsSource = readFileSync(enumsPath, 'utf-8');

function extractEnumMembers(enumName: string): string[] {
  const match = enumsSource.match(new RegExp(`enum ${enumName}\\s*\\{([^}]*)\\}`));
  if (!match) throw new Error(`Could not find enum ${enumName} in RouteEnums.cs`);
  return match[1]!
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean)
    .map((s) => s.charAt(0).toLowerCase() + s.slice(1));
}

// gearLabels is also covered at compile time (Record<Gear, string>, Gear coming from the
// generated schema) — this test only needs to keep verifying orientationLabels, which
// design-system's Compass module owns independently of the backend's generated types
// (its ordering is load-bearing UI geometry, not just a value list — see compass.ts).
describe('orientationLabels', () => {
  it('covers exactly the backend Orientation enum', () => {
    const backendOrientations = extractEnumMembers('Orientation');

    expect(Object.keys(orientationLabels).sort()).toEqual([...backendOrientations].sort());
  });
});

describe('gearLabels', () => {
  it('covers exactly the backend Gear enum', () => {
    const backendGear = extractEnumMembers('Gear');

    expect(Object.keys(gearLabels).sort()).toEqual([...backendGear].sort());
  });
});
