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
