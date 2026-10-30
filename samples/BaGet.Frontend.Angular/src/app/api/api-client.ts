import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of } from 'rxjs';
import { environment } from '../../environments/environment';
import type { ApiPath, CallOptions, HttpMethod, Operation, ResponseOf, ScreenResult, ScreenStatus } from './api-types';

/**
 * Thin typed wrapper over HttpClient. The path, the method, the query/path values, the body and the answer are all checked against
 * the generated types, so a renamed field or a removed route is a compile error after `npm run gen:api`.
 *
 * It never throws for an HTTP error: every error status of `/api/ui` still carries a `ScreenResult` (400 Invalid with the
 * model and errors, 401 SignIn, 403 Forbidden, 404 NotFound), and anything else (503 problem details, network down) is turned
 * into a synthetic one, so a screen only switches on `status`.
 */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl.replace(/\/+$/, '');

  /** The server origin, for the browser navigations that are not XHR (the external sign-in). */
  get baseUrl(): string {
    return this.base;
  }

  get<P extends ApiPath>(path: P, options: CallOptions<Operation<P, 'get'>> = {}): Observable<ResponseOf<Operation<P, 'get'>>> {
    return this.call(path, 'get', options);
  }

  post<P extends ApiPath>(path: P, options: CallOptions<Operation<P, 'post'>> = {}): Observable<ResponseOf<Operation<P, 'post'>>> {
    return this.call(path, 'post', options);
  }

  /** True when `url` is a request to this API (the interceptor adds the token to those only: never to another origin). */
  isApiUrl(url: string): boolean {
    return url.startsWith(this.base + '/api/ui/');
  }

  private call<P extends ApiPath, M extends HttpMethod>(
    path: P,
    method: M,
    options: CallOptions<Operation<P, M>>,
  ): Observable<ResponseOf<Operation<P, M>>> {
    const { url, params } = this.buildUrl(path, options);
    const request = method === 'get' ? this.http.get(url, { params }) : this.http.post(url, options.body ?? {}, { params });
    return request.pipe(catchError((error: unknown) => of(this.toScreenResult(error)))) as Observable<ResponseOf<Operation<P, M>>>;
  }

  /** Fills `{name}` from the path values; what is left of `query` is the query string. */
  private buildUrl(path: string, options: CallOptions<unknown>): { url: string; params: Record<string, string> } {
    const values: Record<string, unknown> = { ...(options.path ?? {}) };
    const url = path.replace(/\{(\w+)\}/g, (_match, name: string) => encodeURIComponent(String(values[name] ?? '')));
    const params: Record<string, string> = {};
    for (const [name, value] of Object.entries(options.query ?? {})) {
      if (value !== undefined && value !== null && value !== '') {
        params[name] = String(value);
      }
    }
    return { url: this.base + url, params };
  }

  private toScreenResult(error: unknown): ScreenResult {
    if (!(error instanceof HttpErrorResponse)) {
      return failure('Invalid', 'Unexpected error.');
    }
    const body: unknown = error.error;
    if (body && typeof body === 'object' && 'status' in body && typeof (body as { status: unknown }).status === 'string') {
      return body as ScreenResult;
    }
    // Problem details (503 while read-only, 400 from model binding) or no body at all (network error, status 0).
    const problem = body && typeof body === 'object' ? (body as { title?: string; detail?: string }) : {};
    const message =
      problem.detail ?? problem.title ?? (error.status === 0 ? 'The server cannot be reached.' : `The request failed (${error.status}).`);
    const status: ScreenStatus =
      error.status === 401 ? 'SignIn' : error.status === 403 ? 'Forbidden' : error.status === 404 ? 'NotFound' : 'Invalid';
    return { ...failure(status, message), statusCode: error.status };
  }
}

function failure(status: ScreenStatus, message: string): ScreenResult {
  return { status, errors: { '': [message] } };
}
