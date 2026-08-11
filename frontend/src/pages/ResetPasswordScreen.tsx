import { useEffect, useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router';
import { Button } from 'acordanza-design-system';
import * as authApi from '../api/auth';
import { ApiError } from '../api/client';
import { errorMessage } from '../api/errorMessage';
import { Brand } from '../components/Brand';

export function ResetPasswordScreen() {
  const [params] = useSearchParams();
  const email = params.get('email');
  const token = params.get('token');

  const [tokenStatus, setTokenStatus] = useState<'checking' | 'valid' | 'invalid' | 'error'>(
    email && token ? 'checking' : 'invalid',
  );
  const [retryCount, setRetryCount] = useState(0);
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState(false);

  useEffect(() => {
    if (!email || !token) return;
    setTokenStatus('checking');
    authApi
      .validateToken(email, token)
      // Only a confirmed 400 from the backend means the token itself is
      // invalid/expired — a network failure or 5xx doesn't tell us that, so
      // it shouldn't present a possibly-valid link as dead with no way back.
      .then(() => setTokenStatus('valid'))
      .catch((err) => setTokenStatus(err instanceof ApiError && err.status === 400 ? 'invalid' : 'error'));
  }, [email, token, retryCount]);

  if (tokenStatus === 'checking') {
    return (
      <div className="login-screen">
        <div className="login-card">
          <div className="brand">
            <Brand />
          </div>
          <p className="login-sub">Comprobando enlace…</p>
        </div>
      </div>
    );
  }

  if (tokenStatus === 'error') {
    return (
      <div className="login-screen">
        <div className="login-card">
          <div className="brand">
            <Brand />
          </div>
          <p className="login-sub">No se pudo comprobar el enlace.</p>
          <Button small fullWidth onClick={() => setRetryCount((c) => c + 1)}>
            Reintentar
          </Button>
        </div>
      </div>
    );
  }

  if (tokenStatus === 'invalid') {
    return (
      <div className="login-screen">
        <div className="login-card">
          <div className="brand">
            <Brand />
          </div>
          <p className="login-sub">El enlace no es válido o ha caducado.</p>
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
      setError(errorMessage(err, 'No se pudo restablecer la contraseña'));
    }
  }

  return (
    <div className="login-screen">
      <div className="login-card">
        <div className="brand">
          <Brand />
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
