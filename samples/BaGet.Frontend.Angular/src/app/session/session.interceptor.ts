import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, tap, throwError } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { SessionService } from './session.service';

/** Set on the one retry, so a request is never retried twice. */
const RETRIED = new HttpContextToken<boolean>(() => false);

/** `/api/ui/session*` endpoints ignore (and must not trigger a refresh for) the access token: the wrong password is a 401 too. */
function isSessionEndpoint(url: string): boolean {
  return /\/api\/ui\/session(\/|\?|$)/.test(url) && !url.includes('/session/sign-out');
}

/**
 * Adds `Authorization: Bearer <access token>` to the calls to the API (and to nothing else), keeps the renewed tokens the
 * server sends in `X-Session-*` headers, and on a 401 refreshes the session once (single flight, see `SessionService.refresh`) and
 * repeats the request. When the refresh is refused the session is over: the person goes to the sign-in page.
 */
export const sessionInterceptor: HttpInterceptorFn = (request, next) => {
  const api = inject(ApiClient);
  const session = inject(SessionService);
  const router = inject(Router);

  if (!api.isApiUrl(request.url)) {
    return next(request);
  }

  const send = (req: HttpRequest<unknown>) => {
    const token = session.token;
    const authorized = token && !isSessionEndpoint(req.url) ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
    return next(authorized).pipe(
      tap((event) => {
        if (event instanceof HttpResponse) {
          session.applyRenewedHeaders(event.headers);
        }
      }),
    );
  };

  return send(request).pipe(
    catchError((error: unknown) => {
      const canRefresh =
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !isSessionEndpoint(request.url) &&
        !request.context.get(RETRIED) &&
        session.hasRefreshToken;
      if (!canRefresh) {
        return throwError(() => error);
      }
      return session.refresh().pipe(
        switchMap((renewed) => {
          if (renewed) {
            return send(request.clone({ context: request.context.set(RETRIED, true) }));
          }
          void router.navigate(['/sign-in'], { queryParams: { returnUrl: router.url } });
          return throwError(() => error);
        }),
      );
    }),
  );
};
