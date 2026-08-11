import { useState } from 'react';
import { errorMessage } from '../api/errorMessage';

// The shape every form modal shares: clear the error, disable the submit while
// in flight, turn a thrown ApiError into Spanish copy, and only run onSuccess
// when the call actually succeeds. No `finally`: a successful submit means the
// modal is about to unmount (or its parent closes it), so resetting `submitting`
// afterwards is at best pointless and at worst a state update on an unmounted
// component — this adopts RejectReasonModal's original behaviour as the rule.
export function useSubmit(fallbackMessage: string) {
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function submit(action: () => Promise<unknown>, onSuccess?: () => void) {
    setError('');
    setSubmitting(true);
    try {
      await action();
      onSuccess?.();
    } catch (err) {
      setError(errorMessage(err, fallbackMessage));
      setSubmitting(false);
    }
  }

  return { error, submitting, submit };
}
