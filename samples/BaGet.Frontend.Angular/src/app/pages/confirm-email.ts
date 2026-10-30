import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { startWith } from 'rxjs';
import { ApiClient } from '../api/api-client';

/**
 * The email link `{SpaBasePath}/confirm-email?userId=&code=[&email=]` -> `GET /api/ui/account/confirmemail` with the same values.
 * A made-up link is the same harmless answer (`succeeded: false`).
 */
@Component({
  selector: 'app-confirm-email',
  imports: [RouterLink],
  template: `
    <h1>Confirm your email</h1>
    @let r = result();
    @if (!r) {
      <p class="muted">Confirming...</p>
    } @else if (r.status === 'Ok' && r.model?.succeeded) {
      <p class="notice">
        @if (r.model.emailChanged) { Your new email address is confirmed. } @else { Your email address is confirmed. }
        @if (r.model.pendingApproval) { An administrator must still approve the account before you can sign in. }
      </p>
      <a routerLink="/sign-in">Sign in</a>
    } @else {
      <p class="error">This confirmation link is not valid (any more).</p>
      <a routerLink="/forgot-password">Request a new confirmation email</a>
    }
  `,
})
export class ConfirmEmail {
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;

  protected readonly result = toSignal(
    inject(ApiClient)
      .get('/api/ui/account/confirmemail', {
        query: {
          userId: this.query.get('userId') ?? undefined,
          code: this.query.get('code') ?? undefined,
          email: this.query.get('email') ?? undefined,
        },
      })
      .pipe(startWith(null)),
    { initialValue: null },
  );
}
