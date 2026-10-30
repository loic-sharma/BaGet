import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { startWith, switchMap } from 'rxjs';
import { ApiClient } from '../api/api-client';

/**
 * `GET /api/ui/admin/users?q=&p=`: administrators only. The page shows what each status means:
 * `SignIn` (401: no or expired session), `Forbidden` (403: signed in, but not an administrator), `Ok` (the list).
 * Writes of this screen (create, approve, promote...) are separate POST twins that also ask the administrator's password.
 */
@Component({
  selector: 'app-admin-users',
  imports: [FormsModule, RouterLink],
  template: `
    <h1>Users</h1>
    @let r = result();
    @if (!r) {
      <p class="muted">Loading...</p>
    } @else if (r.status === 'SignIn') {
      <p class="error">Your session ended. <a [routerLink]="['/sign-in']" [queryParams]="{ returnUrl: '/admin/users' }">Sign in again</a>.</p>
    } @else if (r.status === 'Forbidden') {
      <p class="error">{{ (r.errors?.[''] ?? ['Only administrators can see the users.'])[0] }}</p>
    } @else if (r.status !== 'Ok') {
      <p class="error">{{ (r.errors?.[''] ?? ['The users could not be loaded.'])[0] }}</p>
    } @else {
      @let m = r.model!;
      <form class="row" (ngSubmit)="search()" style="max-width: none">
        <label>Filter <input name="q" [(ngModel)]="q" /></label>
        <button type="submit">Search</button>
      </form>
      @if (m.pendingCount) {
        <p class="notice">{{ m.pendingCount }} account(s) wait for approval.</p>
      }
      <table>
        <thead><tr><th>User</th><th>Email</th><th>Created</th><th>Role</th></tr></thead>
        <tbody>
          @for (u of m.users ?? []; track u.userName) {
            <tr>
              <td>{{ u.userName }}</td>
              <td>{{ u.email }}</td>
              <td>{{ u.created }}</td>
              <td>{{ u.isAdministrator ? 'administrator' : u.pendingApproval ? 'pending approval' : 'user' }}</td>
            </tr>
          }
        </tbody>
      </table>
      <div class="row">
        <button type="button" [disabled]="page() <= 1" (click)="go(page() - 1)">Previous</button>
        <span>Page {{ page() }} of {{ (m.pageCount ?? 1) }}</span>
        <button type="button" [disabled]="page() >= (m.pageCount ?? 1)" (click)="go(page() + 1)">Next</button>
      </div>
    }
  `,
})
export class AdminUsers {
  private readonly api = inject(ApiClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected q = this.route.snapshot.queryParamMap.get('q') ?? '';
  protected readonly page = () => Number(this.route.snapshot.queryParamMap.get('p') ?? 1) || 1;

  protected readonly result = toSignal(
    this.route.queryParamMap.pipe(
      switchMap((p) =>
        this.api.get('/api/ui/admin/users', { query: { q: p.get('q') ?? undefined, p: Number(p.get('p')) || undefined } }).pipe(startWith(null)),
      ),
    ),
    { initialValue: null },
  );

  protected search(): void {
    void this.router.navigate([], { queryParams: { q: this.q || null, p: null } });
  }

  protected go(page: number): void {
    void this.router.navigate([], { queryParams: { p: page > 1 ? page : null }, queryParamsHandling: 'merge' });
  }
}
