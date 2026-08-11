import { useEffect, useState } from 'react';

// Debounce the *value*, not the fetch: a query keyed on the debounced value
// can't be raced by an earlier response landing late, so call sites need no
// `active`/`cancelled`/requestId guard of their own.
export function useDebouncedValue<T>(value: T, delayMs = 250): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const handle = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(handle);
  }, [value, delayMs]);
  return debounced;
}
