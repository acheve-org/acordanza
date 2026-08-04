import { useState, type SubmitEvent } from 'react';
import { Button } from 'acordanza-design-system';
import { useAuth, type PasskeyChallenge } from '../context/AuthContext';
import { ApiError } from '../api/client';
import * as authApi from '../api/auth';

export function LoginScreen() {
  const { login, beginPasskeyLogin, completeLoginWithPasskey } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [challenge, setChallenge] = useState<PasskeyChallenge | null>(null);
  const [verifying, setVerifying] = useState(false);
  const [forgotMode, setForgotMode] = useState(false);
  const [forgotEmail, setForgotEmail] = useState('');
  const [forgotError, setForgotError] = useState('');
  const [forgotSubmitted, setForgotSubmitted] = useState(false);

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

  async function handlePasskeyLogin() {
    setError('');
    try {
      const pending = await beginPasskeyLogin();
      setChallenge(pending);
      await confirmPasskey(pending);
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

  async function handleForgotSubmit(e: SubmitEvent) {
    e.preventDefault();
    setForgotError('');
    try {
      await authApi.forgotPassword(forgotEmail);
      setForgotSubmitted(true);
    } catch (err) {
      setForgotError(err instanceof ApiError ? err.message : 'No se pudo enviar el correo');
    }
  }

  function backToLogin() {
    setForgotMode(false);
    setForgotEmail('');
    setForgotError('');
    setForgotSubmitted(false);
  }

  if (forgotMode) {
    return (
      <div className="login-screen">
        <div className="login-card">
          <div className="brand">
            <span className="brand-mark">&#9670;</span>
            <span className="brand-name">Acordanza</span>
          </div>
          {forgotSubmitted ? (
            <>
              <p className="login-sub">
                Si existe una cuenta con ese correo, te hemos enviado un enlace para restablecer la contraseña.
              </p>
              <Button variant="ghost" small fullWidth type="button" onClick={backToLogin}>
                Volver
              </Button>
            </>
          ) : (
            <>
              <p className="login-sub">Introduce tu correo electrónico y te enviaremos un enlace para restablecerla.</p>
              <form onSubmit={handleForgotSubmit}>
                <label>
                  Correo electrónico
                  <input
                    type="email"
                    required
                    placeholder="alex@example.com"
                    value={forgotEmail}
                    onChange={(e) => setForgotEmail(e.target.value)}
                  />
                </label>
                <Button type="submit">Enviar enlace</Button>
                <p className="error-text">{forgotError}</p>
                <Button variant="ghost" small fullWidth type="button" onClick={backToLogin}>
                  Volver
                </Button>
              </form>
            </>
          )}
        </div>
      </div>
    );
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
            <Button fullWidth type="button" disabled={verifying} onClick={() => confirmPasskey(challenge)}>
              {verifying ? 'Verificando…' : 'Usar llave de acceso'}
            </Button>
            <p className="error-text">{error}</p>
            <Button variant="ghost" small fullWidth type="button" onClick={() => setChallenge(null)}>
              Volver
            </Button>
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
          <Button type="submit">Entrar</Button>
          <p className="error-text">{error}</p>
          <Button variant="ghost" small fullWidth type="button" onClick={() => setForgotMode(true)}>
            ¿Olvidaste tu contraseña?
          </Button>
        </form>
        <div className="login-divider">
          <span>o</span>
        </div>
        <Button variant="ghost" fullWidth type="button" onClick={handlePasskeyLogin}>
          Usar llave de acceso
        </Button>
      </div>
    </div>
  );
}
