import { Component, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { FieldErrors } from '../api/field-errors';
import { Flash, applyServerErrors, safeLocalUrl } from '../api/screens';
import { OIDC_STATE_KEY } from '../session/oidc-state';
import { SessionService, SignInOutcome } from '../session/session.service';

type Step = 'password' | 'code' | 'recovery';

/**
 * Sign-in: user name and password, then the authenticator code (or a recovery code) when the account has two-factor.
 * `POST /api/ui/session` -> tokens, or `{ twoFactorRequired, twoFactorTicket }` -> `POST /session/two-factor` or `/recovery-code`.
 */
@Component({
  selector: 'app-sign-in',
  imports: [ReactiveFormsModule, RouterLink, FieldErrors],
  template: `
    <h1>Sign in</h1>
    @for (message of formErrors(); track message) {
      <p class="error" role="alert">{{ message }}</p>
    }

    @if (step() === 'password') {
      <form [formGroup]="credentials" (ngSubmit)="submitPassword()">
        <label>User name <input formControlName="userName" autocomplete="username" /></label>
        <app-field-errors [control]="credentials.controls.userName" />
        <label>Password <input type="password" formControlName="password" autocomplete="current-password" /></label>
        <app-field-errors [control]="credentials.controls.password" />
        <button type="submit" [disabled]="busy()">Sign in</button>
      </form>
      <p>
        @for (provider of providers(); track provider.id) {
          <button type="button" class="link" (click)="signInWithProvider(provider.id)">Sign in with {{ provider.displayName }}</button>
          -
        }
        <a routerLink="/forgot-password">Forgot your password?</a> - <a routerLink="/register">Register</a>
      </p>
    } @else {
      <form [formGroup]="second" (ngSubmit)="submitSecondFactor()">
        <label>
          {{ step() === 'code' ? 'Authenticator code' : 'Recovery code' }}
          <input formControlName="code" autocomplete="one-time-code" />
        </label>
        <app-field-errors [control]="second.controls.code" />
        <button type="submit" [disabled]="busy()">Verify</button>
      </form>
      <p>
        <button type="button" class="link" (click)="step.set(step() === 'code' ? 'recovery' : 'code')">
          {{ step() === 'code' ? 'Use a recovery code' : 'Use the authenticator code' }}
        </button>
      </p>
    }
  `,
})
export class SignIn {
  private readonly fb = inject(FormBuilder).nonNullable;
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(ApiClient);
  private readonly flash = inject(Flash);

  protected readonly credentials = this.fb.group({
    userName: ['', Validators.required],
    password: ['', Validators.required],
  });
  protected readonly second = this.fb.group({ code: ['', Validators.required] });

  protected readonly step = signal<Step>('password');
  protected readonly busy = signal(false);
  protected readonly formErrors = signal<string[]>([]);
  /** One button per external provider the server offers (the sign-in screen model lists them). */
  protected readonly providers = signal<{ id: string; displayName: string }[]>([]);
  private ticket = '';

  constructor() {
    // Anonymous endpoint; an error answer (the client never throws) just means no external buttons.
    this.api.get('/api/ui/session/providers').subscribe((result) => this.providers.set(result.model?.providers ?? []));
  }

  protected submitPassword(): void {
    if (this.credentials.invalid) {
      this.credentials.markAllAsTouched();
      return;
    }
    const { userName, password } = this.credentials.getRawValue();
    this.run(this.session.signIn(userName, password), this.credentials);
  }

  protected submitSecondFactor(): void {
    if (this.second.invalid) {
      this.second.markAllAsTouched();
      return;
    }
    const { code } = this.second.getRawValue();
    this.run(
      this.step() === 'code'
        ? this.session.signInWithTwoFactor(this.ticket, code)
        : this.session.signInWithRecoveryCode(this.ticket, code),
      this.second,
    );
  }

  /** External provider `id`: a browser navigation (not XHR); the server comes back to `sign-in/external#exchange_token=...`. */
  protected signInWithProvider(id: string): void {
    // A random state binds the answer to this browser: the callback exchanges only when it comes back unchanged.
    const state = Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('');
    sessionStorage.setItem(OIDC_STATE_KEY, state);
    const returnUrl = new URL('sign-in/external', document.baseURI);
    returnUrl.searchParams.set('state', state);
    window.location.assign(`${this.api.baseUrl}/api/ui/session/external/${encodeURIComponent(id)}?returnUrl=${encodeURIComponent(returnUrl.toString())}`);
  }

  private run(outcome$: Observable<SignInOutcome>, form: FormGroup): void {
    this.busy.set(true);
    this.formErrors.set([]);
    outcome$.subscribe((outcome) => {
      this.busy.set(false);
      switch (outcome.kind) {
        case 'signedIn': {
          this.flash.clear();
          const target = safeLocalUrl(this.route.snapshot.queryParamMap.get('returnUrl')) ?? '/packages';
          void this.router.navigateByUrl(target);
          break;
        }
        case 'twoFactor':
          this.ticket = outcome.ticket;
          this.step.set('code');
          break;
        case 'failed':
          // 401 wrong credentials / code, 403 locked out, awaiting approval or email not confirmed, 400 empty form.
          this.formErrors.set(applyServerErrors(form, outcome.result));
          if (!this.formErrors().length && !form.invalid) {
            this.formErrors.set(['Sign-in failed.']);
          }
          break;
      }
    });
  }
}
