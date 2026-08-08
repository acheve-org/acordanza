import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { Permissions } from '../api/permissions';
import { emptyRouteFilters } from '../api/types';
import { Filters } from './Filters';

beforeEach(() => {
  server.use(http.get(`${API_URL}/routes/grades`, () => HttpResponse.json([])));
});

describe('Filters permission gating', () => {
  it('hides + Añadir vía with no permissions', () => {
    renderWithProviders(<Filters filters={emptyRouteFilters} onChange={() => {}} onAddRoute={() => {}} />, {
      auth: { permissions: [] },
    });
    expect(screen.queryByRole('button', { name: '+ Añadir vía' })).not.toBeInTheDocument();
  });

  it('shows + Añadir vía with route.create', () => {
    renderWithProviders(<Filters filters={emptyRouteFilters} onChange={() => {}} onAddRoute={() => {}} />, {
      auth: { permissions: [Permissions.routeCreate] },
    });
    expect(screen.getByRole('button', { name: '+ Añadir vía' })).toBeInTheDocument();
  });
});
