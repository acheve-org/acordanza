import { useState, type FormEvent } from 'react';
import { useAuth } from '../context/AuthContext';
import { ApiError } from '../api/client';

export function LoginScreen() {
  const { login } = useAuth();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError('');
    try {
      await login(username, password);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No se pudo iniciar sesión');
    }
  }

  return (
    <div className="login-screen">
      <div className="login-card">
        <div className="brand">
          <span className="brand-mark">&#9670;</span>
          <span className="brand-name">TOPO</span>
        </div>
        <p className="login-sub">Un registro de vías compartido. Solo para miembros.</p>
        <form onSubmit={handleSubmit}>
          <label>
            Usuario
            <input
              type="text"
              required
              maxLength={40}
              placeholder="alex"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
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
