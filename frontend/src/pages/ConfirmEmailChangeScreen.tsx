import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { Button } from 'acordanza-design-system';
import * as authApi from '../api/auth';
import { Brand } from '../components/Brand';
import { useAuth } from '../hooks/useAuth';

export function ConfirmEmailChangeScreen() {
  const [params] = useSearchParams();
  const uid = params.get('uid');
  const email = params.get('email');
  const token = params.get('token');
  const { username, logout } = useAuth();

  const [status, setStatus] = useState<'checking' | 'success' | 'invalid'>(uid && email && token ? 'checking' : 'invalid');
  // main.tsx mounts under StrictMode, which double-invokes effects in dev —
  // without this latch the second call would fail (the token is single-use)
  // and flip an otherwise-successful confirmation to "invalid".
  const requested = useRef(false);

  useEffect(() => {
    if (!uid || !email || !token) return;
    if (requested.current) return;
    requested.current = true;

    authApi
      .confirmEmailChange(uid, email, token)
      .then(() => setStatus('success'))
      .catch(() => setStatus('invalid'));
  }, [uid, email, token]);

  useEffect(() => {
    if (status === 'success' && username) logout();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status]);

  if (status === 'checking') {
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

  if (status === 'invalid') {
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

  return (
    <div className="login-screen">
      <div className="login-card">
        <div className="brand">
          <Brand />
        </div>
        <p className="success-text">Correo electrónico actualizado. Inicia sesión con tu correo nuevo.</p>
        <Button as={Link} fullWidth to="/">
          Iniciar sesión
        </Button>
      </div>
    </div>
  );
}
