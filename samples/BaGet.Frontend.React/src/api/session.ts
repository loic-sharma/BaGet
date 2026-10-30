import type { components } from './schema';

export type SessionModel = components['schemas']['SessionModel'];

/**
 * Token storage.
 *
 * Trade-off: the access token (15 min) lives in memory only, so it never touches disk, but a script
 * injected into the page can still read it. The refresh token (14 days) is kept in sessionStorage so a
 * reload of the tab stays signed in; sessionStorage is per tab and cleared when the tab closes, but any
 * XSS can read it. localStorage would survive restarts and be shared across tabs (worse); a backend for
 * frontend with an HttpOnly cookie, or BaGet's same-origin cookie mode, avoids script-readable tokens.
 */
const REFRESH_KEY = 'baget.refresh';

export interface SessionState {
  accessToken: string | null;
  refreshToken: string | null;
  userName: string | null;
}

let state: SessionState = { accessToken: null, refreshToken: sessionStorage.getItem(REFRESH_KEY), userName: null };
const listeners = new Set<() => void>();

function set(next: SessionState) {
  state = next;
  if (next.refreshToken) sessionStorage.setItem(REFRESH_KEY, next.refreshToken);
  else sessionStorage.removeItem(REFRESH_KEY);
  listeners.forEach((l) => l());
}

export const sessionStore = {
  subscribe(l: () => void) {
    listeners.add(l);
    return () => {
      listeners.delete(l);
    };
  },
  get: () => state,
  /** Keeps the tokens of a `POST /session*` answer. */
  adopt(m: SessionModel) {
    set({ accessToken: m.accessToken ?? null, refreshToken: m.refreshToken ?? null, userName: m.userName ?? state.userName });
  },
  /** Applies the X-Session-* headers of an answer that renewed the pair (password change, ...). */
  adoptHeaders(h: Headers) {
    const access = h.get('X-Session-Access-Token');
    if (!access) return;
    set({ ...state, accessToken: access, refreshToken: h.get('X-Session-Refresh-Token') ?? state.refreshToken });
  },
  clear() {
    set({ accessToken: null, refreshToken: null, userName: null });
  },
};
