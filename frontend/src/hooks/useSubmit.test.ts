import { act, renderHook } from '@testing-library/react';
import { useSubmit } from './useSubmit';
import { ApiError } from '../api/client';

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

describe('useSubmit', () => {
  it('is submitting during the call and false after a failure', async () => {
    const { promise, reject } = deferred<void>();
    const { result } = renderHook(() => useSubmit('fallback'));

    act(() => {
      void result.current.submit(() => promise);
    });
    expect(result.current.submitting).toBe(true);

    await act(async () => {
      reject(new Error('boom'));
      await promise.catch(() => {});
    });
    expect(result.current.submitting).toBe(false);
  });

  it('runs onSuccess on success and not on failure', async () => {
    const { result } = renderHook(() => useSubmit('fallback'));
    const onSuccess = vi.fn();

    await act(async () => {
      await result.current.submit(() => Promise.resolve(), onSuccess);
    });
    expect(onSuccess).toHaveBeenCalledTimes(1);

    await act(async () => {
      await result.current.submit(() => Promise.reject(new Error('boom')), onSuccess);
    });
    expect(onSuccess).toHaveBeenCalledTimes(1);
  });

  it('carries the ApiError message, and falls back to the given string for a non-ApiError throw', async () => {
    const { result } = renderHook(() => useSubmit('mensaje por defecto'));

    await act(async () => {
      await result.current.submit(() => Promise.reject(new ApiError(400, 'mensaje del backend')));
    });
    expect(result.current.error).toBe('mensaje del backend');

    await act(async () => {
      await result.current.submit(() => Promise.reject(new Error('boom')));
    });
    expect(result.current.error).toBe('mensaje por defecto');
  });

  it('clears the previous error on a second submit', async () => {
    const { result } = renderHook(() => useSubmit('fallback'));

    await act(async () => {
      await result.current.submit(() => Promise.reject(new Error('boom')));
    });
    expect(result.current.error).toBe('fallback');

    const { promise } = deferred<void>();
    act(() => {
      void result.current.submit(() => promise);
    });
    expect(result.current.error).toBe('');
  });
});
