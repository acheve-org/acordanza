// Mirrors backend/Acordanza.Api/Authorization/Permissions.cs
export const Permissions = {
  cragCreate: 'crag.create',
  cragEdit: 'crag.edit',
  cragValidate: 'crag.validate',
  cragDelete: 'crag.delete',

  routeCreate: 'route.create',
  routeEdit: 'route.edit',
  routeValidate: 'route.validate',
  routeDelete: 'route.delete',

  tickCreate: 'tick.create',
  tickEdit: 'tick.edit',
  tickValidate: 'tick.validate',
  tickDelete: 'tick.delete',

  openerCreate: 'opener.create',
  openerEdit: 'opener.edit',
  openerValidate: 'opener.validate',
  openerDelete: 'opener.delete',

  userManage: 'user.manage',
} as const;
