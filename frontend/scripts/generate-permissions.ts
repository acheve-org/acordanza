// Generates src/api/permissions.ts from backend/Acordanza.Api/Authorization/Permissions.cs
// (DUP-12) — the flat `{entity}.{action}` permission-string catalogue used to be
// hand-mirrored here, with only a source-code comment ("Mirrors backend/...") holding the
// two files in sync. There's no OpenAPI document for a plain string constant list (unlike
// generate-api's `bunx openapi-typescript ...`, which regenerates from the backend's
// actual OpenAPI schema), so this is a small standalone script instead — same shape as
// generate-api: run it, then `openapi-contract-check`-style CI fails if the committed
// output drifts from what it produces (see .github/workflows/ci-cd.yml).
//
// Run via `bun run generate-permissions`.
import { readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const permissionsPath = path.resolve(
  import.meta.dirname,
  '../../backend/Acordanza.Api/Authorization/Permissions.cs',
);
const source = readFileSync(permissionsPath, 'utf-8');

// One entry per `public const string Name = "value";` line (skips `All`, which is a
// IReadOnlyList<string>, not a const string, and isn't matched by this pattern). A blank
// source line between two const lines starts a new group — reproduced here so the
// generated file keeps the same entity-by-entity grouping as Permissions.cs.
const constLine = /^\s*public const string (\w+) = "([^"]+)";\s*$/;

type Entry = { name: string; value: string; startsGroup: boolean };
const entries: Entry[] = [];
let pendingBlank = false;

for (const line of source.split('\n')) {
  const match = line.match(constLine);
  if (match) {
    const [, name, value] = match as unknown as [string, string, string];
    entries.push({ name, value, startsGroup: pendingBlank && entries.length > 0 });
    pendingBlank = false;
  } else if (line.trim() === '') {
    pendingBlank = true;
  }
}

if (entries.length === 0) {
  throw new Error(`Found no "public const string X = "y.z";" lines in ${permissionsPath}`);
}

// PascalCase (C# field name) -> camelCase (JS key), e.g. "FirstAscensionistLinkApprove"
// -> "firstAscensionistLinkApprove".
const toCamelCase = (name: string) => name.charAt(0).toLowerCase() + name.slice(1);

const body = entries
  .map((entry, i) => `${entry.startsGroup && i > 0 ? '\n' : ''}  ${toCamelCase(entry.name)}: '${entry.value}',`)
  .join('\n');

const output = `// GENERATED FILE — do not edit by hand.
// Source: backend/Acordanza.Api/Authorization/Permissions.cs
// Regenerate with \`bun run generate-permissions\` from frontend/ (scripts/generate-permissions.ts).
// openapi-contract-check-style CI (.github/workflows/ci-cd.yml) fails the build if this
// file is stale relative to that source.
export const Permissions = {
${body}
} as const;
`;

const outPath = path.resolve(import.meta.dirname, '../src/api/permissions.ts');
writeFileSync(outPath, output);
console.log(`Wrote ${entries.length} permissions to ${path.relative(process.cwd(), outPath)}`);
