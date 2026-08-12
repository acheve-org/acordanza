import { useState } from 'react';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
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

describe('Filters orientation picker', () => {
  it('hides the clear button when nothing is selected', () => {
    renderWithProviders(<Filters filters={emptyRouteFilters} onChange={() => {}} onAddRoute={() => {}} />);
    expect(screen.queryByRole('button', { name: 'Cualquiera' })).not.toBeInTheDocument();
  });

  it('selecting two sectors calls onChange with both in compass order', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    // Filters is a controlled component — a spy `onChange` alone doesn't feed the next
    // selection back in, so this wrapper re-renders with each update the way SearchPage
    // does, to exercise picking south then north (out of compass order) for real.
    function Controlled() {
      const [filters, setFilters] = useState(emptyRouteFilters);
      return (
        <Filters
          filters={filters}
          onChange={(next) => {
            setFilters(next);
            onChange(next);
          }}
          onAddRoute={() => {}}
        />
      );
    }
    renderWithProviders(<Controlled />);

    await user.click(screen.getByRole('checkbox', { name: 'Sur' }));
    expect(onChange).toHaveBeenLastCalledWith({ ...emptyRouteFilters, orientation: ['south'] });

    await user.click(screen.getByRole('checkbox', { name: 'Norte' }));
    expect(onChange).toHaveBeenLastCalledWith({ ...emptyRouteFilters, orientation: ['north', 'south'] });
  });

  it('shows the clear button once something is selected, resetting to []', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const { rerender } = renderWithProviders(
      <Filters filters={{ ...emptyRouteFilters, orientation: ['north'] }} onChange={onChange} onAddRoute={() => {}} />,
    );

    const clearButton = screen.getByRole('button', { name: 'Cualquiera' });
    await user.click(clearButton);

    expect(onChange).toHaveBeenCalledWith({ ...emptyRouteFilters, orientation: [] });
    rerender(<Filters filters={emptyRouteFilters} onChange={onChange} onAddRoute={() => {}} />);
    expect(screen.queryByRole('button', { name: 'Cualquiera' })).not.toBeInTheDocument();
  });
});
