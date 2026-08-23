// GENERATED FILE — do not edit by hand.
// Source: backend/Acordanza.Api/Authorization/Permissions.cs
// Regenerate with `bun run generate-permissions` from frontend/ (scripts/generate-permissions.ts).
// openapi-contract-check-style CI (.github/workflows/ci-cd.yml) fails the build if this
// file is stale relative to that source.
export const Permissions = {
  areaCreate: 'area.create',
  areaEdit: 'area.edit',
  areaValidate: 'area.validate',
  areaDelete: 'area.delete',

  routeCreate: 'route.create',
  routeEdit: 'route.edit',
  routeValidate: 'route.validate',
  routeDelete: 'route.delete',
  routeEnrich: 'route.enrich',

  tickCreate: 'tick.create',
  tickEdit: 'tick.edit',
  tickValidate: 'tick.validate',
  tickDelete: 'tick.delete',

  firstAscensionistCreate: 'first-ascensionist.create',
  firstAscensionistEdit: 'first-ascensionist.edit',
  firstAscensionistValidate: 'first-ascensionist.validate',
  firstAscensionistDelete: 'first-ascensionist.delete',

  userManage: 'user.manage',
  userInvite: 'user.invite',

  areaExpertRequest: 'area-expert.request',
  areaExpertApprove: 'area-expert.approve',

  firstAscensionistLinkRequest: 'first-ascensionist-link.request',
  firstAscensionistLinkApprove: 'first-ascensionist-link.approve',
} as const;
