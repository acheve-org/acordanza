import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router';
import { Button } from 'acordanza-design-system';
import * as authApi from '../api/auth';
import { ApiError } from '../api/client';

export function ResetPasswordScreen() {
  const [params] = useSearchParams();
  const email = params.get('email');
  const token = params.get('token');

  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState(false);

  if (!email || !token) {
    return (
      <div className="login-screen">
        <div className="login-card">
          <div className="brand">
            <span className="brand-mark">&#9670;</span>
            <span className="brand-name">Acordanza</span>
          </div>
          <p className="login-sub">El enlace no es válido.</p>
          <Button as={Link} variant="ghost" small fullWidth to="/">
            Volver al inicio de sesión
          </Button>
        </div>
      </div>
    );
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError('');

    if (newPassword !== confirmPassword) {
      setError('Las contraseñas no coinciden');
      return;
    }

    try {
      await authApi.resetPassword(email!, token!, newPassword);
      setSuccess(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo restablecer la contraseña');
    }
  }

  return (
    <div className="login-screen">
      <div className="login-card">
        <div className="brand">
          <span className="brand-mark">&#9670;</span>
          <span className="brand-name">Acordanza</span>
        </div>
        {success ? (
          <>
            <p className="success-text">Contraseña actualizada. Ya puedes iniciar sesión.</p>
            <Button as={Link} fullWidth to="/">
              Iniciar sesión
            </Button>
          </>
        ) : (
          <>
            <p className="login-sub">Elige una nueva contraseña.</p>
            <form onSubmit={handleSubmit}>
              <label>
                Contraseña nueva
                <input
                  type="password"
                  required
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                />
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
              <Button type="submit">Guardar contraseña</Button>
              <p className="error-text">{error}</p>
            </form>
          </>
        )}
      </div>
    </div>
  );
}
