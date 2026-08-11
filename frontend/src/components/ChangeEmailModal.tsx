import { useState, type FormEvent } from 'react';
import { Button, Modal, ModalActions } from 'acordanza-design-system';
import * as accountApi from '../api/account';
import { errorMessage } from '../api/errorMessage';

interface Props {
  onClose: () => void;
}

type Step = 'info' | 'form' | 'sent';

export function ChangeEmailModal({ onClose }: Props) {
  const [step, setStep] = useState<Step>('info');
  const [newEmail, setNewEmail] = useState('');
  const [currentPassword, setCurrentPassword] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError('');
    setSubmitting(true);
    try {
      await accountApi.requestEmailChange(newEmail.trim(), currentPassword);
      setStep('sent');
    } catch (err) {
      setError(errorMessage(err, 'No se pudo solicitar el cambio de correo'));
    } finally {
      setSubmitting(false);
    }
  }

  if (step === 'info') {
    return (
      <Modal onClose={onClose}>
        <h2>Cambiar correo electrónico</h2>
        <p>Tu correo electrónico es también tu usuario: es lo que escribes para iniciar sesión.</p>
        <ul>
          <li>
            Te enviaremos un enlace de confirmación al correo nuevo. Nada cambia hasta que lo abras, y el enlace
            caduca en 1 hora.
          </li>
          <li>A partir de entonces iniciarás sesión con el correo nuevo y ahí recibirás los avisos de la aplicación.</li>
          <li>
            Tus llaves de acceso seguirán funcionando. Puede que tu gestor de contraseñas siga mostrando el correo
            antiguo junto a ellas: es solo la etiqueta que guardó al crearlas.
          </li>
          <li>El resto de tu cuenta no cambia: tus vías, tus aperturas, tus sectores y tus invitaciones siguen igual.</li>
        </ul>
        <p>También avisaremos a tu correo actual de que has pedido el cambio.</p>
        <ModalActions>
          <Button variant="ghost" type="button" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="button" onClick={() => setStep('form')}>
            Continuar
          </Button>
        </ModalActions>
      </Modal>
    );
  }

  if (step === 'sent') {
    return (
      <Modal onClose={onClose}>
        <h2>Cambiar correo electrónico</h2>
        <p>Te hemos enviado un enlace a {newEmail}. Ábrelo para confirmar el cambio.</p>
        <ModalActions>
          <Button type="button" onClick={onClose}>
            Entendido
          </Button>
        </ModalActions>
      </Modal>
    );
  }

  return (
    <Modal onClose={onClose}>
      <h2>Cambiar correo electrónico</h2>
      <form onSubmit={handleSubmit}>
        <label>
          Correo electrónico nuevo
          <input
            type="email"
            required
            value={newEmail}
            onChange={(e) => setNewEmail(e.target.value)}
          />
        </label>
        <label>
          Contraseña actual
          <input
            type="password"
            required
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
          />
        </label>
        <p className="error-text">{error}</p>
        <ModalActions>
          <Button variant="ghost" type="button" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={submitting}>
            {submitting ? 'Enviando…' : 'Enviar enlace'}
          </Button>
        </ModalActions>
      </form>
    </Modal>
  );
}
