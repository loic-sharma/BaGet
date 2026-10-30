import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiClient } from '../api/api-client';
import { FieldErrors } from '../api/field-errors';
import { Flash, applyServerErrors } from '../api/screens';

/**
 * The email link `{SpaBasePath}/reset-password?userId=&code=`. The `code` is posted back unchanged with the new password
 * (`POST /api/ui/account/resetpassword`); a bad or expired code is `Invalid` with the reason in `errors`.
 */
@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink, FieldErrors],
  template: `
    <h1>Choose a new password</h1>
    @for (message of formErrors(); track message) {
      <p class="error" role="alert">{{ message }}</p>
    }
    @if (!userId || !code) {
      <p class="error">This link is incomplete. <a routerLink="/forgot-password">Ask for a new one.</a></p>
    } @else {
      <form [formGroup]="form" (ngSubmit)="submit()">
        <label>New password <input type="password" formControlName="password" autocomplete="new-password" /></label>
        <app-field-errors [control]="form.controls.password" />
        <label>Confirm password <input type="password" formControlName="confirmPassword" autocomplete="new-password" /></label>
        <app-field-errors [control]="form.controls.confirmPassword" />
        <button type="submit" [disabled]="busy()">Reset password</button>
      </form>
    }
  `,
})
export class ResetPassword {
  private readonly api = inject(ApiClient);
  private readonly router = inject(Router);
  private readonly flash = inject(Flash);
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;

  protected readonly userId = this.query.get('userId');
  protected readonly code = this.query.get('code');

  protected readonly form = inject(FormBuilder).nonNullable.group({
    password: ['', Validators.required],
    confirmPassword: ['', Validators.required],
  });
  protected readonly busy = signal(false);
  protected readonly formErrors = signal<string[]>([]);

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    const { password, confirmPassword } = this.form.getRawValue();
    this.api
      .post('/api/ui/account/resetpassword', { body: { userId: this.userId, code: this.code, password, confirmPassword } })
      .subscribe((result) => {
        this.busy.set(false);
        if (result.status === 'Invalid' || result.status === 'Forbidden' || result.status === 'NotFound') {
          this.formErrors.set(applyServerErrors(this.form, result));
          return;
        }
        this.flash.show(result.next?.message ?? 'Your password was changed. Sign in with the new one.');
        void this.router.navigate(['/sign-in']);
      });
  }
}
