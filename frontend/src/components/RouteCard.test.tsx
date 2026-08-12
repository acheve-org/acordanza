import { render, screen } from '@testing-library/react';
import { makeRoute } from '../test/factories';
import { RouteCard } from './RouteCard';

describe('RouteCard validation status chip', () => {
  it('renders a status chip for a pending route', () => {
    render(<RouteCard route={makeRoute({ validationStatus: 'pending' })} onClick={() => {}} />);

    expect(screen.getByText('Pendiente de validación')).toBeInTheDocument();
  });

  it('renders a status chip for a rejected route', () => {
    render(<RouteCard route={makeRoute({ validationStatus: 'rejected' })} onClick={() => {}} />);

    expect(screen.getByText('Rechazado')).toBeInTheDocument();
  });

  it('renders no status chip for an approved route', () => {
    render(<RouteCard route={makeRoute({ validationStatus: 'approved' })} onClick={() => {}} />);

    expect(screen.queryByText('Pendiente de validación')).not.toBeInTheDocument();
    expect(screen.queryByText('Rechazado')).not.toBeInTheDocument();
    expect(screen.queryByText('Validado')).not.toBeInTheDocument();
  });
});

describe('RouteCard grade band', () => {
  it('applies no grade-band class for an ungraded/below-6a route', () => {
    const { container } = render(<RouteCard route={makeRoute({ grade: '4a' })} onClick={() => {}} />);
    expect(container.querySelector('.route-card')?.className).not.toMatch(/grade-band-/);
  });

  it('applies grade-band-6 for a 6th-grade route', () => {
    const { container } = render(<RouteCard route={makeRoute({ grade: '6a' })} onClick={() => {}} />);
    expect(container.querySelector('.route-card')).toHaveClass('grade-band-6');
  });

  it('applies grade-band-7 for a 7th-grade route', () => {
    const { container } = render(<RouteCard route={makeRoute({ grade: '7b+' })} onClick={() => {}} />);
    expect(container.querySelector('.route-card')).toHaveClass('grade-band-7');
  });

  it('applies grade-band-8-9 for an 8th/9th-grade route', () => {
    const { container } = render(<RouteCard route={makeRoute({ grade: '8a' })} onClick={() => {}} />);
    expect(container.querySelector('.route-card')).toHaveClass('grade-band-8-9');
  });
});

describe('RouteCard meta line', () => {
  it('renders the orientation needle, abbreviation, length, and gear when present', () => {
    render(<RouteCard route={makeRoute({ orientation: 'south', length: 28, gear: 'bolted' })} onClick={() => {}} />);
    expect(screen.getByRole('img', { name: 'Orientación: Sur' })).toBeInTheDocument();
    expect(screen.getByText('S')).toBeInTheDocument();
    expect(screen.getByText('28 m')).toBeInTheDocument();
    expect(screen.getByText('Equipada')).toBeInTheDocument();
  });

  it('omits the meta line when orientation, length, and gear are all absent', () => {
    const { container } = render(
      <RouteCard route={makeRoute({ orientation: null, length: 0, gear: null })} onClick={() => {}} />,
    );
    expect(container.querySelector('.rc-meta')).not.toBeInTheDocument();
  });
});
