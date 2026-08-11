import { act, renderHook } from '@testing-library/react';
import { useDebouncedValue } from './useDebouncedValue';

describe('useDebouncedValue', () => {
  it('updates once after the delay, not per keystroke', () => {
    vi.useFakeTimers();
    try {
      const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 250), {
        initialProps: { value: 't' },
      });
      expect(result.current).toBe('t');

      for (const value of ['tr', 'tra']) {
        rerender({ value });
        act(() => {
          vi.advanceTimersByTime(100); // < 250ms: debounce keeps resetting
        });
      }
      expect(result.current).toBe('t'); // unchanged so far

      act(() => {
        vi.advanceTimersByTime(250);
      });
      expect(result.current).toBe('tra');
    } finally {
      vi.useRealTimers();
    }
  });
});
