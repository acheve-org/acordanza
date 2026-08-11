// Mirrors backend/Acordanza.Api/Authorization/Permissions.cs
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
