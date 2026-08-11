import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import { ApiError } from '../api/client';
import { AddTickModal } from './AddTickModal';

describe('AddTickModal', () => {
  it('submits the entered date/rating/notes and calls onClose on success', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    const onClose = vi.fn();
    renderWithProviders(<AddTickModal routeName="Travesía del Gato" onClose={onClose} onSubmit={onSubmit} />);

    fireEvent.change(screen.getByLabelText('Fecha'), { target: { value: '2026-01-01' } });
    fireEvent.change(screen.getByLabelText(/Puntuación/), { target: { value: '4' } });
    fireEvent.change(screen.getByLabelText('Notas'), { target: { value: 'Buena vía' } });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar' }));

    await waitFor(() =>
      expect(onSubmit).toHaveBeenCalledWith({ date: '2026-01-01', rating: 4, notes: 'Buena vía' }),
    );
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it('sends rating: null when the optional rating field is left empty', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    renderWithProviders(<AddTickModal routeName="Travesía del Gato" onClose={vi.fn()} onSubmit={onSubmit} />);

    fireEvent.change(screen.getByLabelText('Fecha'), { target: { value: '2026-01-01' } });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar' }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledWith({ date: '2026-01-01', rating: null, notes: '' }));
  });

  it('surfaces a failure and does not close', async () => {
    const onSubmit = vi.fn().mockRejectedValue(new ApiError(400, 'No se pudo'));
    const onClose = vi.fn();
    renderWithProviders(<AddTickModal routeName="Travesía del Gato" onClose={onClose} onSubmit={onSubmit} />);

    fireEvent.change(screen.getByLabelText('Fecha'), { target: { value: '2026-01-01' } });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar' }));

    await waitFor(() => expect(screen.getByText('No se pudo')).toBeInTheDocument());
    expect(onClose).not.toHaveBeenCalled();
  });
});
