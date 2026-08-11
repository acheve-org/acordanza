import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import { ApiError } from '../api/client';
import { RejectReasonModal } from './RejectReasonModal';

describe('RejectReasonModal', () => {
  it('disables submit until a reason is typed', () => {
    renderWithProviders(<RejectReasonModal title="Rechazar vía" onClose={vi.fn()} onReject={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'Rechazar solicitud' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Motivo del rechazo'), { target: { value: 'Falta información' } });
    expect(screen.getByRole('button', { name: 'Rechazar solicitud' })).not.toBeDisabled();
  });

  it('calls onReject with the trimmed reason', async () => {
    const onReject = vi.fn().mockResolvedValue(undefined);
    renderWithProviders(<RejectReasonModal title="Rechazar vía" onClose={vi.fn()} onReject={onReject} />);

    fireEvent.change(screen.getByLabelText('Motivo del rechazo'), { target: { value: '  Falta información  ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Rechazar solicitud' }));

    await waitFor(() => expect(onReject).toHaveBeenCalledWith('Falta información'));
  });

  it('surfaces a failure and re-enables the button', async () => {
    const onReject = vi.fn().mockRejectedValue(new ApiError(400, 'No se pudo procesar'));
    renderWithProviders(<RejectReasonModal title="Rechazar vía" onClose={vi.fn()} onReject={onReject} />);

    fireEvent.change(screen.getByLabelText('Motivo del rechazo'), { target: { value: 'Falta información' } });
    fireEvent.click(screen.getByRole('button', { name: 'Rechazar solicitud' }));

    await waitFor(() => expect(screen.getByText('No se pudo procesar')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Rechazar solicitud' })).not.toBeDisabled();
  });
});
