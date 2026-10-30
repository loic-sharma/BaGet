import { routeFor } from '../routes';
import type { components } from './schema';

export type Status = components['schemas']['ScreenStatus'];
export type ScreenNext = components['schemas']['ScreenNext'];

/** The shape of every /api/ui answer; the generated client types `model` per endpoint. */
export interface ScreenResultLike<T> {
  status?: Status;
  model?: T | null;
  errors?: { [field: string]: string[] } | null;
  next?: ScreenNext | null;
}

/** What the screens work with: the status, the HTTP status and the typed model. */
export interface Screen<T> {
  status: Status;
  http: number;
  model: T | null;
  errors: Record<string, string[]>;
  next: ScreenNext | null;
}

/**
 * Turns an openapi-fetch answer into a `Screen`. The document describes 200 and the 400/401/403/404
 * errors with the same ScreenResult schema, so `data` or `error` holds a typed body. The status is
 * inferred from the HTTP code only when there is no body (a proxy error, a 503).
 */
export function toScreen<T>(r: { data?: ScreenResultLike<T>; error?: ScreenResultLike<T>; response: Response }): Screen<T> {
  const body = r.data ?? r.error;
  const http = r.response.status;
  const status: Status = body?.status ?? (http === 401 ? 'SignIn' : http === 403 ? 'Forbidden' : http === 404 ? 'NotFound' : http === 400 ? 'Invalid' : 'Ok');
  return { status, http, model: body?.model ?? null, errors: body?.errors ?? {}, next: body?.next ?? null };
}

/** The message of a refusal: the form-level error, else a generic text for the status. */
export function refusalText(s: Screen<unknown>): string {
  const form = s.errors[''];
  if (form?.length) return form.join(' ');
  if (s.status === 'SignIn') return 'Sign in to see this.';
  if (s.status === 'Forbidden') return 'You may not see this.';
  if (s.status === 'NotFound') return 'Not found.';
  return 'The server refused the request (HTTP ' + s.http + ').';
}

/** The SPA route a successful write sends the user to, or undefined when it stays here. */
export function nextRoute(s: Screen<unknown>): string | undefined {
  return s.next?.screen ? routeFor(s.next.screen, s.next.routeValues ?? {}) : undefined;
}
