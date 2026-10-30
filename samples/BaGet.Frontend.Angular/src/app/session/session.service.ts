import { HttpHeaders } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, finalize, map, of, shareReplay, tap } from 'rxjs';
import { ApiClient } from '../api/api-client';
import type { ScreenResult, SessionModel } from '../api/api-types';

const REFRESH_KEY = 'baget.refreshToken';

export type SignInOutcome =
  | { kind: 'signedIn' }
  | { kind: 'twoFactor'; ticket: string }
  | { kind: 'failed'; result: ScreenResult };

/**
 * The interactive session of a person (`POST /api/ui/session`).
 *
 * Where the tokens live (the trade-off):
 *  - the access token (15 minutes) stays in memory only: a script injected into the page can still use it while the page is open,
 *    but it is gone with the tab and never written anywhere;
 *  - the refresh token (14 days) is kept in sessionStorage so a reload or F5 does not sign the person out. sessionStorage is per
 *    tab and dies with it, but is readable by any script of this origin (XSS), unlike an HttpOnly cookie. If that is not
 *    acceptable, keep the refresh token in memory too (a reload then signs out) or serve the application from BaGet's own origin and use
 *    the cookie mode (`POST /api/ui/session/cookie` + `X-XSRF-TOKEN`, see README).
 * The server never stores a token: "sign out" forgets them here, "sign out everywhere" changes the account's security stamp.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly api = inject(ApiClient);

  private readonly accessToken = signal<string | null>(null);
  private readonly refreshToken = signal<string | null>(sessionStorage.getItem(REFRESH_KEY));

  /** The signed-in user name (the sign-in answers carry it; after a reload it may be unknown until the first answer that does). */
  readonly userName = signal<string | null>(null);
  readonly isSignedIn = computed(() => this.accessToken() !== null);

  private refreshing: Observable<boolean> | null = null;

  /** The token the interceptor adds, if any. */
  get token(): string | null {
    return this.accessToken();
  }

  get hasRefreshToken(): boolean {
    return this.refreshToken() !== null;
  }

  /** Startup: a refresh token left by a reload is exchanged for an access token before the first guard runs. */
  restore(): Observable<boolean> {
    return this.hasRefreshToken ? this.refresh() : of(false);
  }

  signIn(userName: string, password: string): Observable<SignInOutcome> {
    return this.api.post('/api/ui/session', { body: { userName, password } }).pipe(map((r) => this.outcome(r)));
  }

  signInWithTwoFactor(ticket: string, code: string): Observable<SignInOutcome> {
    return this.api.post('/api/ui/session/two-factor', { body: { ticket, code } }).pipe(map((r) => this.outcome(r)));
  }

  signInWithRecoveryCode(ticket: string, recoveryCode: string): Observable<SignInOutcome> {
    return this.api.post('/api/ui/session/recovery-code', { body: { ticket, recoveryCode } }).pipe(map((r) => this.outcome(r)));
  }

  /** The 60-second token an external provider's sign-in left in the URL fragment. */
  exchange(exchangeToken: string): Observable<SignInOutcome> {
    return this.api.post('/api/ui/session/exchange', { body: { exchangeToken } }).pipe(map((r) => this.outcome(r)));
  }

  /** Takes the tokens of a session answer (the sign-in forms and a registration that signs in at once). */
  adopt(session: SessionModel | null | undefined): boolean {
    if (!session?.accessToken || !session.refreshToken) {
      return false;
    }
    this.store(session.accessToken, session.refreshToken, session.userName ?? null);
    return true;
  }

  /**
   * Renews the pair with the refresh token. Single flight: while one refresh runs every caller (several requests failing with
   * 401 at once) shares it, because a second use of the same refresh token would just be a second pair. A refused refresh
   * (expired, password changed, signed out everywhere, 30-day cap) ends the session locally.
   */
  refresh(): Observable<boolean> {
    if (this.refreshing) {
      return this.refreshing;
    }
    const token = this.refreshToken();
    if (!token) {
      this.clear();
      return of(false);
    }
    this.refreshing = this.api.post('/api/ui/session/refresh', { body: { refreshToken: token } }).pipe(
      map((result) => {
        if (result.status === 'Ok' && this.adopt(result.model)) {
          return true;
        }
        this.clear();
        return false;
      }),
      finalize(() => (this.refreshing = null)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.refreshing;
  }

  /**
   * Picks the renewed pair from the response headers. The server sets them when the current session itself changed the account's
   * security (password change, two-factor, sign out everywhere): the old tokens are dead, replace them.
   */
  applyRenewedHeaders(headers: HttpHeaders): void {
    const access = headers.get('X-Session-Access-Token');
    const refresh = headers.get('X-Session-Refresh-Token');
    if (access && refresh) {
      this.store(access, refresh, this.userName());
    }
  }

  /** Signs out: tells the server (best effort) and forgets the tokens. `everywhere` ends every other session of the account too. */
  signOut(everywhere = false): Observable<void> {
    const body = { everywhere, refreshToken: this.refreshToken() ?? undefined };
    return this.api.post('/api/ui/session/sign-out', { body }).pipe(
      tap(() => this.clear()),
      map(() => undefined),
    );
  }

  /** Forgets the session without asking the server (it ended there already, or the page is closing). */
  clear(): void {
    this.accessToken.set(null);
    this.refreshToken.set(null);
    this.userName.set(null);
    sessionStorage.removeItem(REFRESH_KEY);
  }

  private outcome(result: ScreenResult<SessionModel>): SignInOutcome {
    const model = result.model;
    if (result.status === 'Ok' && model?.twoFactorRequired && model.twoFactorTicket) {
      return { kind: 'twoFactor', ticket: model.twoFactorTicket };
    }
    if (result.status === 'Ok' && this.adopt(model)) {
      return { kind: 'signedIn' };
    }
    return { kind: 'failed', result };
  }

  private store(access: string, refresh: string, userName: string | null): void {
    this.accessToken.set(access);
    this.refreshToken.set(refresh);
    sessionStorage.setItem(REFRESH_KEY, refresh);
    if (userName) {
      this.userName.set(userName);
    }
  }
}
