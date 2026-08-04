import { useState, type FormEvent } from 'react';
import { Button, Modal, ModalActions } from 'acordanza-design-system';
import * as usersApi from '../api/users';
import { roleLabels } from '../api/types';
import { ApiError } from '../api/client';

interface Props {
  onClose: () => void;
  onInvited: () => void;
}

export function InviteUserModal({ onClose, onInvited }: Props) {
  const [email, setEmail] = useState('');
  const [role, setRole] = useState('user');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError('');
    setSubmitting(true);
    try {
      await usersApi.inviteUser({ email: email.trim(), role });
      onInvited();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo enviar la invitación');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Modal onClose={onClose}>
      <h2>Invitar usuario</h2>
      <form onSubmit={handleSubmit}>
        <label>
          Correo electrónico
          <input
            type="email"
            required
            placeholder="alex@example.com"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
        </label>
        <label>
          Rol
          <select value={role} onChange={(e) => setRole(e.target.value)}>
            {Object.entries(roleLabels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <p className="error-text">{error}</p>
        <ModalActions>
          <Button variant="ghost" type="button" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={submitting}>
            {submitting ? 'Enviando…' : 'Enviar invitación'}
          </Button>
        </ModalActions>
      </form>
    </Modal>
  );
}
