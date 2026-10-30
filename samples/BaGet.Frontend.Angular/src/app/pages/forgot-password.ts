import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiClient } from '../api/api-client';
import { FieldErrors } from '../api/field-errors';
import { applyServerErrors } from '../api/screens';

/**
 * `POST /api/ui/account/forgotpassword` and `POST /api/ui/account/resend-confirmation` take `{ identifier }` (user name or email).
 * The answer is the same whether or not an account matches, so the page says "if it exists".
 */
@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink, FieldErrors],
  template: `
    <h1>Forgot your password?</h1>
    @for (message of formErrors(); track message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    @if (notice(); as text) {
      <p class="notice">{{ text }}</p>
    }
    <form [formGroup]="form" (ngSubmit)="send('forgot')">
      <label>User name or email <input formControlName="identifier" /></label>
      <app-field-errors [control]="form.controls.identifier" />
      <div class="row">
        <button type="submit" [disabled]="busy()">Email me a reset link</button>
        <button type="button" [disabled]="busy()" (click)="send('resend')">Resend the confirmation email</button>
      </div>
    </form>
    <p><a routerLink="/sign-in">Back to sign-in</a></p>
  `,
})
export class ForgotPassword {
  private readonly api = inject(ApiClient);

  protected readonly form = inject(FormBuilder).nonNullable.group({ identifier: ['', Validators.required] });
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly formErrors = signal<string[]>([]);

  protected send(kind: 'forgot' | 'resend'): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.notice.set(null);
    const body = this.form.getRawValue();
    const call = kind === 'forgot'
      ? this.api.post('/api/ui/account/forgotpassword', { body })
      : this.api.post('/api/ui/account/resend-confirmation', { body });
    call.subscribe((result) => {
      this.busy.set(false);
      if (result.status === 'Invalid' || result.status === 'Forbidden' || result.status === 'NotFound') {
        this.formErrors.set(applyServerErrors(this.form, result));
      } else {
        this.formErrors.set([]);
        this.notice.set(
          kind === 'forgot'
            ? 'If an account matches, a reset link is on its way.'
            : 'If an unconfirmed account matches, a new confirmation link is on its way.',
        );
      }
    });
  }
}
