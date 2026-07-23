import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import { create, type CredentialCreationOptionsJSON } from '@github/webauthn-json';
import * as accountApi from '../api/account';
import * as passkeysApi from '../api/passkeys';
import type { PasskeySummary } from '../api/types';
import { ApiError } from '../api/client';

export function SettingsPage() {
  return (
    <main className="settings-page">
      <h1>Configuración</h1>
      <PasswordSection />
      <PasskeysSection />
      <Link to="/" className="ghost-btn small">
        Volver
      </Link>
    </main>
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
      setError(err instanceof ApiError ? err.message : 'No se pudo cambiar la contraseña');
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
        <div className="modal-actions">
          <button type="submit" className="primary-btn">
            Guardar contraseña
          </button>
        </div>
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
      setError(err instanceof ApiError ? err.message : 'No se pudo registrar la llave de acceso');
    } finally {
      setRegistering(false);
    }
  }

  async function handleDelete(id: number) {
    if (!confirm('¿Eliminar esta llave de acceso?')) return;
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
        <p className="empty-state">No tienes ninguna llave de acceso registrada</p>
      ) : (
        <ul className="passkey-list">
          {passkeys.map((p) => (
            <li key={p.id} className="tick-row">
              <div>
                <div className="tr-who">{p.name}</div>
                <div className="tr-notes">Añadida el {new Date(p.createdAt).toLocaleDateString('es-ES')}</div>
              </div>
              <button
                type="button"
                className="tick-remove"
                aria-label="Eliminar llave de acceso"
                onClick={() => handleDelete(p.id)}
              >
                &times;
              </button>
            </li>
          ))}
        </ul>
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
        <button type="submit" className="primary-btn" disabled={registering}>
          {registering ? 'Esperando confirmación…' : 'Añadir llave de acceso'}
        </button>
        <p className="error-text">{error}</p>
      </form>
    </section>
  );
}
