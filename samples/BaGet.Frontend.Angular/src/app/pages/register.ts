import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiClient } from '../api/api-client';
import { FieldErrors } from '../api/field-errors';
import { Flash, applyServerErrors, follow } from '../api/screens';
import { SessionService } from '../session/session.service';

/**
 * `POST /api/ui/account/register`. Answers `model.session` (tokens: signed in at once) when the account is usable, otherwise the
 * flags `confirmationSent` / `awaitingApproval`. `Invalid` carries `errors` per field. `404` = registration is closed.
 */
@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, FieldErrors],
  template: `
    <h1>Register</h1>
    @for (message of formErrors(); track message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    @if (done(); as text) {
      <p class="notice">{{ text }}</p>
      <a routerLink="/sign-in">Sign in</a>
    } @else {
      <form [formGroup]="form" (ngSubmit)="submit()">
        <label>User name <input formControlName="userName" autocomplete="username" /></label>
        <app-field-errors [control]="form.controls.userName" />
        <label>Email <input type="email" formControlName="email" autocomplete="email" /></label>
        <app-field-errors [control]="form.controls.email" />
        <label>Password <input type="password" formControlName="password" autocomplete="new-password" /></label>
        <app-field-errors [control]="form.controls.password" />
        <label>Confirm password <input type="password" formControlName="confirmPassword" autocomplete="new-password" /></label>
        <app-field-errors [control]="form.controls.confirmPassword" />
        <button type="submit" [disabled]="busy()">Register</button>
      </form>
    }
  `,
})
export class Register {
  private readonly api = inject(ApiClient);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly flash = inject(Flash);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    userName: ['', Validators.required],
    email: [''],
    password: ['', Validators.required],
    confirmPassword: ['', Validators.required],
  });
  protected readonly busy = signal(false);
  protected readonly formErrors = signal<string[]>([]);
  protected readonly done = signal<string | null>(null);

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.api.post('/api/ui/account/register', { body: this.form.getRawValue() }).subscribe((result) => {
      this.busy.set(false);
      const model = result.model;
      if (result.status === 'Invalid' || result.status === 'Forbidden' || result.status === 'NotFound') {
        this.formErrors.set(applyServerErrors(this.form, result));
        if (result.status === 'NotFound') {
          this.formErrors.set(['Registration is closed.']);
        }
      } else if (this.session.adopt(model?.session)) {
        follow(this.router, this.flash, result);
      } else if (model?.awaitingApproval) {
        this.done.set('Account created. An administrator must approve it before you can sign in.');
      } else if (model?.confirmationSent) {
        this.done.set('Account created. Follow the link in the email we sent you to confirm your address.');
      } else {
        follow(this.router, this.flash, result, ['/sign-in']);
      }
    });
  }
}
