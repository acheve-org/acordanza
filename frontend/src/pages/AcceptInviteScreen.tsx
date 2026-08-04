import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router';
import { Button } from 'acordanza-design-system';
import * as authApi from '../api/auth';
import { ApiError } from '../api/client';

export function AcceptInviteScreen() {
  const [params] = useSearchParams();
  const email = params.get('email');
  const token = params.get('token');

  const [name, setName] = useState('');
  const [password, setPassword] = useState('');
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

    if (password !== confirmPassword) {
      setError('Las contraseñas no coinciden');
      return;
    }

    try {
      await authApi.acceptInvite(email!, token!, name.trim(), password);
      setSuccess(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo completar el registro');
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
            <p className="success-text">Cuenta creada. Ya puedes iniciar sesión.</p>
            <Button as={Link} fullWidth to="/">
              Iniciar sesión
            </Button>
          </>
        ) : (
          <>
            <p className="login-sub">Completa tu registro en Acordanza.</p>
            <form onSubmit={handleSubmit}>
              <label>
                Nombre
                <input
                  type="text"
                  required
                  maxLength={20}
                  placeholder="Alex"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                />
              </label>
              <label>
                Contraseña
                <input type="password" required value={password} onChange={(e) => setPassword(e.target.value)} />
              </label>
              <label>
                Repite la contraseña
                <input
                  type="password"
                  required
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                />
              </label>
              <Button type="submit">Crear cuenta</Button>
              <p className="error-text">{error}</p>
            </form>
          </>
        )}
      </div>
    </div>
  );
}
