import { useState, type FormEvent } from 'react';
import { Button, Modal, ModalActions } from 'acordanza-design-system';
import * as usersApi from '../api/users';
import { roleLabels } from '../api/types';
import { useSubmit } from '../hooks/useSubmit';

interface Props {
  onClose: () => void;
  onInvited: () => void;
  /** Hides the role picker — non-admin invites are always created as 'user'. */
  allowRoleSelection?: boolean;
}

export function InviteUserModal({ onClose, onInvited, allowRoleSelection = true }: Props) {
  const [email, setEmail] = useState('');
  const [role, setRole] = useState('user');
  const { error, submitting, submit } = useSubmit('No se pudo enviar la invitación');

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    await submit(() => usersApi.inviteUser({ email: email.trim(), role }), onInvited);
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
        {allowRoleSelection && (
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
        )}
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
