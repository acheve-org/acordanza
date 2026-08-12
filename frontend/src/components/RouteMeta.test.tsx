import { render, screen } from '@testing-library/react';
import { RouteMeta } from './RouteMeta';

describe('RouteMeta', () => {
  it('renders the needle with its accessible name when orientation is set', () => {
    render(<RouteMeta orientation="south" length={28} gear="bolted" />);
    expect(screen.getByRole('img', { name: 'Orientación: Sur' })).toBeInTheDocument();
    expect(screen.getByText('S · 28 m · Equipada')).toBeInTheDocument();
  });

  it('renders nothing when orientation, length, and gear are all absent', () => {
    const { container } = render(<RouteMeta orientation={null} length={0} gear={null} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders no needle when orientation alone is absent', () => {
    render(<RouteMeta orientation={null} length={28} gear="bolted" />);
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.getByText('28 m · Equipada')).toBeInTheDocument();
  });
});
