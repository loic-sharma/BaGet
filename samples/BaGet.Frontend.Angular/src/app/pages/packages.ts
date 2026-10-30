import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map, startWith, switchMap } from 'rxjs';
import { ApiClient } from '../api/api-client';

/** The home screen twin: `GET /api/ui/packages?q=&p=&prerelease=&sortBy=` (anonymous). The state lives in the URL. */
@Component({
  selector: 'app-packages',
  imports: [FormsModule, RouterLink],
  template: `
    <h1>Packages</h1>
    <form class="row" (ngSubmit)="search()" style="max-width: none">
      <label>Search <input name="q" [(ngModel)]="q" /></label>
      <label>Sort
        <select name="sort" [(ngModel)]="sort">
          <option value="">Relevance</option>
          <option value="TotalDownloads">Most downloaded</option>
        </select>
      </label>
      <label><span><input type="checkbox" name="pre" [(ngModel)]="prerelease" /> Pre-release</span></label>
      <button type="submit">Search</button>
    </form>

    @let r = result();
    @if (!r) {
      <p class="muted">Loading...</p>
    } @else if (r.status !== 'Ok') {
      <p class="error">{{ (r.errors?.[''] ?? ['The packages could not be loaded.'])[0] }}</p>
    } @else {
      <ul class="packages">
        @for (p of r.model?.packages ?? []; track p.id) {
          <li>
            <a [routerLink]="['/packages', p.id]"><strong>{{ p.title || p.id }}</strong></a>
            <span class="muted"> {{ p.version }} - {{ p.totalDownloads }} downloads</span>
            <div>{{ p.summary || p.description }}</div>
          </li>
        } @empty {
          <li class="muted">No package found.</li>
        }
      </ul>
      <div class="row">
        <button type="button" [disabled]="page() <= 1" (click)="go(page() - 1)">Previous</button>
        <span>Page {{ page() }}</span>
        <button type="button" [disabled]="(r.model?.packages?.length ?? 0) === 0" (click)="go(page() + 1)">Next</button>
      </div>
    }
  `,
})
export class Packages {
  private readonly api = inject(ApiClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected q = this.route.snapshot.queryParamMap.get('q') ?? '';
  protected sort = this.route.snapshot.queryParamMap.get('sortBy') ?? '';
  protected prerelease = this.route.snapshot.queryParamMap.get('prerelease') === 'true';

  private readonly params = toSignal(this.route.queryParamMap.pipe(map((p) => p)), { requireSync: true });
  protected readonly page = () => Number(this.params().get('p') ?? 1) || 1;

  protected readonly result = toSignal(
    this.route.queryParamMap.pipe(
      switchMap((p) =>
        this.api
          .get('/api/ui/packages', {
            query: {
              q: p.get('q') ?? undefined,
              p: p.get('p') ?? undefined,
              prerelease: p.get('prerelease') === 'true' ? true : undefined,
              // The OpenAPI document types the enum as a number; the server binds its name from the query string.
              sortBy: (p.get('sortBy') ?? undefined) as never,
            },
          })
          .pipe(startWith(null)),
      ),
    ),
    { initialValue: null },
  );

  protected search(): void {
    void this.router.navigate([], {
      queryParams: { q: this.q || null, sortBy: this.sort || null, prerelease: this.prerelease ? 'true' : null, p: null },
    });
  }

  protected go(page: number): void {
    void this.router.navigate([], { queryParams: { p: page > 1 ? page : null }, queryParamsHandling: 'merge' });
  }
}
