import { Component, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { combineLatest, startWith, switchMap } from 'rxjs';
import { ApiClient } from '../api/api-client';

/**
 * `GET /api/ui/packages/{id}` (latest) or `/api/ui/packages/{id}/{version}`. `id` and `version` come from the route through `withComponentInputBinding()`.
 * A missing package is `NotFound` (or `Ok` with `found: false`).
 */
@Component({
  selector: 'app-package-detail',
  imports: [RouterLink],
  template: `
    @let r = result();
    @if (!r) {
      <p class="muted">Loading...</p>
    } @else if (r.status === 'NotFound' || r.model?.found === false) {
      <h1>{{ id() }}</h1>
      <p class="error">This package (or version) does not exist.</p>
      <a routerLink="/packages">Back to the list</a>
    } @else if (r.status !== 'Ok') {
      <p class="error">{{ (r.errors?.[''] ?? ['The package could not be loaded.'])[0] }}</p>
    } @else {
      @let m = r.model!;
      <h1>{{ m.package?.id }} <small class="muted">{{ m.package?.version }}</small></h1>
      <p>{{ m.package?.description }}</p>
      <p class="muted">{{ m.totalDownloads }} downloads - {{ (m.package?.authors ?? []).join(', ') }}</p>

      @for (tab of m.installTabs ?? []; track tab.name) {
        <h3>{{ tab.name }}</h3>
        <pre>{{ (tab.lines ?? []).join('\\n') }}</pre>
      }

      <h3>Versions</h3>
      <ul>
        @for (v of m.versions ?? []; track v.version) {
          <li>
            <a [routerLink]="['/packages', m.package?.id, v.version]">{{ v.version }}</a>
            <span class="muted"> - {{ v.downloads }} downloads</span>
          </li>
        }
      </ul>
    }
  `,
})
export class PackageDetail {
  private readonly api = inject(ApiClient);

  readonly id = input.required<string>();
  readonly version = input<string>();

  protected readonly result = toSignal(
    combineLatest([toObservable(this.id), toObservable(this.version)]).pipe(
      switchMap(([id, version]) => (version ? this.api.get('/api/ui/packages/{id}/{version}', { path: { id, version } }) : this.api.get('/api/ui/packages/{id}', { path: { id } })).pipe(
          startWith(null),
        ),
      ),
    ),
    { initialValue: null },
  );
}
