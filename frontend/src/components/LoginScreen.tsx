import { useState, type SubmitEvent } from 'react';
import { useAuth, type PasskeyChallenge } from '../context/AuthContext';
import { ApiError } from '../api/client';

export function LoginScreen() {
  const { login, completeLoginWithPasskey } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [challenge, setChallenge] = useState<PasskeyChallenge | null>(null);
  const [verifying, setVerifying] = useState(false);

  async function handleSubmit(e: SubmitEvent) {
    e.preventDefault();
    setError('');
    try {
      const pending = await login(email, password);
      if (pending) {
        setChallenge(pending);
        await confirmPasskey(pending);
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo iniciar sesión');
    }
  }

  async function confirmPasskey(pending: PasskeyChallenge) {
    setVerifying(true);
    setError('');
    try {
      await completeLoginWithPasskey(pending);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo verificar la llave de acceso');
    } finally {
      setVerifying(false);
    }
  }

  if (challenge) {
    return (
      <div className="login-screen">
        <div className="login-card">
          <div className="brand">
            <span className="brand-mark">&#9670;</span>
            <span className="brand-name">Acordanza</span>
          </div>
          <p className="login-sub">Confirma tu identidad con tu llave de acceso.</p>
          <div className="login-mfa">
            <button
              type="button"
              className="primary-btn full-width"
              disabled={verifying}
              onClick={() => confirmPasskey(challenge)}
            >
              {verifying ? 'Verificando…' : 'Usar llave de acceso'}
            </button>
            <p className="error-text">{error}</p>
            <button type="button" className="ghost-btn small full-width" onClick={() => setChallenge(null)}>
              Volver
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="login-screen">
      <div className="login-card">
        <div className="brand">
          <span className="brand-mark">&#9670;</span>
          <span className="brand-name">Acordanza</span>
        </div>
        <p className="login-sub">Encuentra la ruta que quieres escalar. Solo para miembros.</p>
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
            Contraseña
            <input
              type="password"
              required
              placeholder="••••••"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </label>
          <button type="submit" className="primary-btn">
            Entrar
          </button>
          <p className="error-text">{error}</p>
        </form>
      </div>
    </div>
  );
}
