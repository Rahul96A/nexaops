import { useEffect, useState } from 'react';

/**
 * Delays a rapidly changing value.
 *
 * Used for search boxes, so typing a ticket number issues one query rather than one per
 * keystroke - which matters both for the database and for the API rate limit.
 */
export function useDebounced<T>(value: T, delayMs = 300): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delayMs);
    return () => window.clearTimeout(timer);
  }, [value, delayMs]);

  return debounced;
}
