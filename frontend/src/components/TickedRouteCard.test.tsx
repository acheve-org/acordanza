import { render, screen } from '@testing-library/react';
import { makeTickedRoute } from '../test/factories';
import { TickedRouteCard } from './TickedRouteCard';

describe('TickedRouteCard meta line', () => {
  it('renders orientation and length from MyTickDto', () => {
    render(
      <TickedRouteCard
        tick={makeTickedRoute({ orientation: 'south', length: 28, gear: 'bolted' })}
        onSelect={() => {}}
      />,
    );
    expect(screen.getByRole('img', { name: 'Orientación: Sur' })).toBeInTheDocument();
    expect(screen.getByText('S')).toBeInTheDocument();
    expect(screen.getByText('28 m')).toBeInTheDocument();
    expect(screen.getByText('Equipada')).toBeInTheDocument();
  });

  it('omits the meta line when orientation, length, and gear are all absent', () => {
    const { container } = render(
      <TickedRouteCard
        tick={makeTickedRoute({ orientation: null, length: 0, gear: null })}
        onSelect={() => {}}
      />,
    );
    expect(container.querySelector('.rc-meta')).not.toBeInTheDocument();
  });
});
