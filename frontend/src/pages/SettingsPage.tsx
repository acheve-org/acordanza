import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import { create, type CredentialCreationOptionsJSON } from '@github/webauthn-json';
import { Button, ConfirmModal, EmptyState, List, ListRow, ModalActions, RemoveButton } from 'acordanza-design-system';
import * as accountApi from '../api/account';
import * as passkeysApi from '../api/passkeys';
import type { PasskeySummary } from '../api/types';
import { errorMessage } from '../api/errorMessage';
import { useAuth } from '../context/AuthContext';
import { formatDate } from '../format';
import { ChangeEmailModal } from '../components/ChangeEmailModal';

export function SettingsPage() {
  return (
    <main className="settings-page">
      <h1>Configuración</h1>
      <ProfileSection />
      <PasswordSection />
      <PasskeysSection />
      <Button as={Link} variant="ghost" small to="/">
        Volver
      </Button>
    </main>
  );
}

function ProfileSection() {
  const { name, username, updateName } = useAuth();
  const [value, setValue] = useState(name ?? '');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState(false);
  const [changingEmail, setChangingEmail] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError('');
    setSuccess(false);

    try {
      const res = await accountApi.updateProfile(value.trim());
      updateName(res.name);
      setValue(res.name);
      setSuccess(true);
    } catch (err) {
      setError(errorMessage(err, 'No se pudo guardar el nombre'));
    }
  }

  return (
    <section className="settings-section">
      <h2>Perfil</h2>
      {/* No separate "email" field on MeResponse — username already is the account's email. */}
      <div className="field-static">
        <span className="field-static-label">Correo electrónico</span>
        <span>{username}</span>
      </div>
      <Button variant="ghost" small onClick={() => setChangingEmail(true)}>
        Cambiar correo electrónico
      </Button>
      {changingEmail && <ChangeEmailModal onClose={() => setChangingEmail(false)} />}
      <form onSubmit={handleSubmit}>
        <label>
          Nombre
          <input type="text" required maxLength={20} value={value} onChange={(e) => setValue(e.target.value)} />
        </label>
        <ModalActions>
          <Button type="submit">Guardar nombre</Button>
        </ModalActions>
        <p className="error-text">{error}</p>
        {success && <p className="success-text">Nombre actualizado</p>}
      </form>
    </section>
  );
}

function PasswordSection() {
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError('');
    setSuccess(false);

    if (newPassword !== confirmPassword) {
      setError('Las contraseñas nuevas no coinciden');
      return;
    }

    try {
      await accountApi.changePassword(currentPassword, newPassword);
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      setSuccess(true);
    } catch (err) {
      setError(errorMessage(err, 'No se pudo cambiar la contraseña'));
    }
  }

  return (
    <section className="settings-section">
      <h2>Contraseña</h2>
      <form onSubmit={handleSubmit}>
        <label>
          Contraseña actual
          <input
            type="password"
            required
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
          />
        </label>
        <div className="grid-2">
          <label>
            Contraseña nueva
            <input type="password" required value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
          </label>
          <label>
            Repite la contraseña nueva
            <input
              type="password"
              required
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
            />
          </label>
        </div>
        <ModalActions>
          <Button type="submit">Guardar contraseña</Button>
        </ModalActions>
        <p className="error-text">{error}</p>
        {success && <p className="success-text">Contraseña actualizada</p>}
      </form>
    </section>
  );
}

function PasskeysSection() {
  const [passkeys, setPasskeys] = useState<PasskeySummary[]>([]);
  const [newPasskeyName, setNewPasskeyName] = useState('');
  const [registering, setRegistering] = useState(false);
  const [error, setError] = useState('');
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<number | null>(null);

  useEffect(() => {
    passkeysApi.listPasskeys().then(setPasskeys);
  }, []);

  async function handleRegister(e: FormEvent) {
    e.preventDefault();
    setError('');
    setRegistering(true);
    try {
      const { sessionId, options } = await passkeysApi.getRegisterOptions(newPasskeyName.trim());
      const attestationResponse = await create({ publicKey: options } as CredentialCreationOptionsJSON);
      await passkeysApi.completeRegister(sessionId, attestationResponse);
      setNewPasskeyName('');
      setPasskeys(await passkeysApi.listPasskeys());
    } catch (err) {
      setError(errorMessage(err, 'No se pudo registrar la llave de acceso'));
    } finally {
      setRegistering(false);
    }
  }

  async function handleDelete(id: number) {
    await passkeysApi.deletePasskey(id);
    setPasskeys(await passkeysApi.listPasskeys());
  }

  return (
    <section className="settings-section">
      <h2>Llaves de acceso</h2>
      <p className="hint">
        Añade una llave de acceso (huella, Face ID, Windows Hello o una llave física) como segundo factor al iniciar
        sesión con tu contraseña.
      </p>

      {passkeys.length === 0 ? (
        <EmptyState>No tienes ninguna llave de acceso registrada</EmptyState>
      ) : (
        <List>
          {passkeys.map((p) => (
            <ListRow
              key={p.id}
              title={p.name}
              subtitle={`Añadida el ${formatDate(p.createdAt)}`}
              trailing={
                <RemoveButton label="Eliminar llave de acceso" onClick={() => setConfirmingDeleteId(p.id)} />
              }
            />
          ))}
        </List>
      )}

      {confirmingDeleteId !== null && (
        <ConfirmModal
          message="¿Eliminar esta llave de acceso?"
          confirmLabel="Eliminar"
          danger
          onConfirm={() => handleDelete(confirmingDeleteId)}
          onClose={() => setConfirmingDeleteId(null)}
        />
      )}

      <form onSubmit={handleRegister} className="passkey-form">
        <label>
          Nombre de la llave
          <input
            type="text"
            required
            placeholder="MacBook, iPhone…"
            value={newPasskeyName}
            onChange={(e) => setNewPasskeyName(e.target.value)}
          />
        </label>
        <Button type="submit" disabled={registering}>
          {registering ? 'Esperando confirmación…' : 'Añadir llave de acceso'}
        </Button>
        <p className="error-text">{error}</p>
      </form>
    </section>
  );
}
