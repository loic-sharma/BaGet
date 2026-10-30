import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Flash, safeLocalUrl } from '../api/screens';
import { OIDC_STATE_KEY } from '../session/oidc-state';
import { SessionService } from '../session/session.service';

/**
 * Where the server returns after an external provider's sign-in (`returnUrl` of `GET /api/ui/session/external/{provider}`):
 * `.../sign-in/external#exchange_token=...&exchange_expires_at=...&user_name=...` or `#notice=...` / `#error=...`.
 * The `state` of the query must equal the one saved in `sessionStorage` before the redirect, else nothing is exchanged. The fragment
 * is removed from the address bar and the history at once, then the 60-second (single-use) token is exchanged for the session
 * (`POST /api/ui/session/exchange`). No session token ever travels in a URL.
 */
@Component({
  selector: 'app-external-sign-in',
  imports: [RouterLink],
  template: `
    <h1>External sign-in</h1>
    @if (message(); as text) {
      <p [class]="failed() ? 'error' : 'notice'">{{ text }}</p>
      <a routerLink="/sign-in">Back to sign-in</a>
    } @else {
      <p class="muted">Signing in...</p>
    }
  `,
})
export class ExternalSignIn {
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly flash = inject(Flash);

  protected readonly message = signal<string | null>(null);
  protected readonly failed = signal(false);

  constructor() {
    const fragment = new URLSearchParams(window.location.hash.replace(/^#/, ''));
    const state = new URLSearchParams(window.location.search).get('state');
    const expected = sessionStorage.getItem(OIDC_STATE_KEY);
    sessionStorage.removeItem(OIDC_STATE_KEY);
    // Out of the history and the Referer before anything else happens (the state is spent too).
    history.replaceState(null, '', window.location.pathname);
    if (!expected || state !== expected) {
      this.failed.set(true);
      this.message.set('External sign-in refused: the state does not match. Start again.');
      return;
    }

    const token = fragment.get('exchange_token');
    const notice = fragment.get('notice');
    const error = fragment.get('error');
    if (!token) {
      this.failed.set(!!error || !notice);
      this.message.set(error ?? notice ?? 'There is nothing to sign in with.');
      return;
    }
    this.session.exchange(token).subscribe((outcome) => {
      if (outcome.kind === 'signedIn') {
        this.flash.clear();
        void this.router.navigateByUrl(safeLocalUrl(this.route.snapshot.queryParamMap.get('returnUrl')) ?? '/packages', { replaceUrl: true });
      } else {
        // 'twoFactor' would need the code step of the sign-in page; this sample only reports it.
        this.failed.set(true);
        this.message.set(outcome.kind === 'twoFactor' ? 'This account needs a second factor: sign in with the password form.' : 'The sign-in link expired or was already used.');
      }
    });
  }
}
