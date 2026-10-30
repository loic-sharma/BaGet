import createClient, { type Middleware } from 'openapi-fetch';
import { baseUrl } from '../config';
import type { paths } from './schema';
import { sessionStore } from './session';

export const api = createClient<paths>({ baseUrl });

const isSessionCall = (url: string) => new URL(url, location.href).pathname.includes('/api/ui/session');

// A request is consumed when sent, so keep a clone to replay it after a refresh.
const replayable = new WeakMap<Request, Request>();
// One refresh at a time: concurrent 401s share the same promise.
let refreshing: Promise<boolean> | null = null;

/** Exchanges the refresh token for a new pair. False = no new tokens (cleared when the server refused). */
export function refreshSession(): Promise<boolean> {
  refreshing ??= (async () => {
    const refreshToken = sessionStore.get().refreshToken;
    if (!refreshToken) return false;
    const { data, response } = await api.POST('/api/ui/session/refresh', { body: { refreshToken } });
    if (response.ok && data?.model?.accessToken) {
      sessionStore.adopt(data.model);
      return true;
    }
    // Only a refusal ends the session; a network error or a 5xx leaves the tokens for a retry.
    if (response.status === 401 || response.status === 403) sessionStore.clear();
    return false;
  })().finally(() => {
    refreshing = null;
  });
  return refreshing;
}

const auth: Middleware = {
  onRequest({ request }) {
    replayable.set(request, request.clone());
    const { accessToken } = sessionStore.get();
    if (accessToken) request.headers.set('Authorization', `Bearer ${accessToken}`);
    return request;
  },
  async onResponse({ request, response }) {
    // Renewed pair after a password / two-factor change made by this very session.
    sessionStore.adoptHeaders(response.headers);
    if (response.status !== 401 || isSessionCall(request.url) || !sessionStore.get().refreshToken) return response;
    if (!(await refreshSession())) return response;
    const retry = replayable.get(request);
    if (!retry) return response;
    retry.headers.set('Authorization', `Bearer ${sessionStore.get().accessToken}`);
    return fetch(retry); // once: a second 401 is returned as is
  },
};

api.use(auth);
