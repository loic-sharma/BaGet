import { useEffect, useState } from 'react';
import type { Screen } from '../api/screen';

/** Loads a screen when `deps` change; `null` while loading. A network failure becomes an Invalid screen. */
export function useScreen<T>(load: () => Promise<Screen<T>>, deps: unknown[]): Screen<T> | null {
  const [screen, setScreen] = useState<Screen<T> | null>(null);
  useEffect(() => {
    let live = true;
    setScreen(null);
    load()
      .catch((e: unknown): Screen<T> => ({ status: 'Invalid', http: 0, model: null, errors: { '': [String(e)] }, next: null }))
      .then((s) => {
        if (live) setScreen(s);
      });
    return () => {
      live = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
  return screen;
}
