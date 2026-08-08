import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../test/server';
import { API_URL } from '../test/apiUrl';
import { renderWithProviders } from '../test/renderWithProviders';
import { makeAreaNode, makePagedAreaNodes } from '../test/factories';
import { AreaTree } from './AreaTree';

describe('AreaTree deferred fetching', () => {
  it('loads roots on mount but fetches a node\'s children only once it is expanded', async () => {
    let childrenCalls = 0;
    server.use(
      http.get(`${API_URL}/areas/roots`, () =>
        HttpResponse.json(
          makePagedAreaNodes({
            items: [makeAreaNode({ id: 1, name: 'Peña Rúa', hasChildren: true })],
            totalCount: 1,
          }),
        ),
      ),
      http.get(`${API_URL}/areas/1/children`, () => {
        childrenCalls++;
        return HttpResponse.json([makeAreaNode({ id: 2, name: 'Sector 1', hasChildren: false })]);
      }),
    );

    renderWithProviders(<AreaTree canCreate={false} canEdit={false} canDelete={false} />);

    expect(await screen.findByText('Peña Rúa')).toBeInTheDocument();
    expect(childrenCalls).toBe(0);

    fireEvent.click(screen.getByRole('button', { name: 'Expandir' }));

    await waitFor(() => expect(childrenCalls).toBe(1));
    expect(await screen.findByText('Sector 1')).toBeInTheDocument();
  });

  it('does not re-fetch children on collapse/re-expand — only the first expand loads them', async () => {
    let childrenCalls = 0;
    server.use(
      http.get(`${API_URL}/areas/roots`, () =>
        HttpResponse.json(
          makePagedAreaNodes({
            items: [makeAreaNode({ id: 1, name: 'Peña Rúa', hasChildren: true })],
            totalCount: 1,
          }),
        ),
      ),
      http.get(`${API_URL}/areas/1/children`, () => {
        childrenCalls++;
        return HttpResponse.json([makeAreaNode({ id: 2, name: 'Sector 1', hasChildren: false })]);
      }),
    );

    renderWithProviders(<AreaTree canCreate={false} canEdit={false} canDelete={false} />);
    await screen.findByText('Peña Rúa');

    const toggle = screen.getByRole('button', { name: 'Expandir' });
    fireEvent.click(toggle); // expand — fetches
    await waitFor(() => expect(childrenCalls).toBe(1));
    fireEvent.click(screen.getByRole('button', { name: 'Contraer' })); // collapse — no fetch
    fireEvent.click(screen.getByRole('button', { name: 'Expandir' })); // re-expand — no fetch, cached

    expect(childrenCalls).toBe(1);
  });
});
