# Building a custom frontend (React, Angular)

Every BaGet screen is a thin MVC view over a JSON endpoint under `/api/ui` that does the work. A single-page application (SPA) can call
the same endpoints and replace the HTML screens, or add to them. Two working samples are in the repository:

| Sample | Stack |
|---|---|
| [`samples/BaGet.Frontend.React`](https://github.com/pieroviano/BaGet/tree/main/samples/BaGet.Frontend.React) | Vite, React 18, TypeScript, react-router, `openapi-fetch` |
| [`samples/BaGet.Frontend.Angular`](https://github.com/pieroviano/BaGet/tree/main/samples/BaGet.Frontend.Angular) | Angular (standalone components, signals), `HttpClient` with a typed wrapper |

Reference: [UI API](ui-api.md) (endpoints, who may call, sessions), [configuration](configuration.md#single-page-applications) (settings).

## 1. Choose how the SPA signs in

| Mode | Use when | How |
|---|---|---|
| **Session token** (recommended) | The SPA runs on any origin (its own server, a CDN, a dev server) or on BaGet's | `POST /api/ui/session` returns an access token (15 min) and a refresh token (14 days, capped by `Accounts:SessionMaxLifetime`). Send `Authorization: Bearer bgs.…` on every `/api/ui` call |
| **Cookie + antiforgery** | The SPA is served by BaGet itself (same origin) and you prefer an HttpOnly cookie to a token in JavaScript | `GET /api/ui/antiforgery`, then `POST /api/ui/session/cookie` with the `X-XSRF-TOKEN` header; afterwards every `/api/ui` call sends the cookie **and** the header |

API keys also work on `/api/ui` (scripts, kiosks), but an API key is refused on the account and package-ownership screens: a person must sign in.

## 2. Configure BaGet

| Setting | Development (SPA on `http://localhost:5173`) | Production |
|---|---|---|
| `Cors:AllowedOrigins` | `http://localhost:5173` | the SPA's origin, or empty when BaGet serves it |
| `Frontend:SpaPath` | (empty) | folder holding the built `index.html` (BaGet serves it) |
| `Frontend:SpaBasePath` | | `/app` (default); the SPA's routes live below it |
| `Frontend:Mvc` | `true` | `false` to run the SPA alone (the HTML screens are removed) |
| `Frontend:SpaUrl` | `http://localhost:5173` | absolute address of an SPA hosted elsewhere; emails link to it |
| `Frontend:SpaLinks` | | `true` (default when `Mvc=false` and BaGet serves the SPA): emails link to the SPA routes below |
| `Site:PublicUrl` | | the public address; email links and the same-origin check use it |

With `Frontend:Mvc=false` and email configured, BaGet refuses to start until it knows where the SPA lives (`SpaPath`, `SpaUrl` or `SpaLinks`).

Notes:
- With `Cors:AllowedOrigins` empty, a page of another origin **cannot** call the session and account endpoints. A separate-origin SPA must be listed. A listed origin gets credentialed access to `/api/ui` only.
- Behind a TLS proxy, configure the trusted proxy (`Proxy:*`) or `Site:PublicUrl`, otherwise the server sees `http` and refuses `https` origins.
- The SPA must implement these routes under `SpaBasePath`, used by emails and redirects: `confirm-email?userId=&code=[&email=]`, `reset-password?userId=&code=`, `sign-in`.

## 3. Generate a typed client

The OpenAPI document `ui` describes every endpoint, its body and its `ScreenResult<T>` answer (success and error statuses).

```bash
# An administrator's API key with the admin:api scope (or the global ApiKey) can read the document.
curl -H "X-NuGet-ApiKey: $KEY" https://baget.example.com/openapi/ui.json -o openapi/ui.json
npx openapi-typescript openapi/ui.json -o src/api/schema.d.ts
```

Commit `openapi/ui.json` and the generated `schema.d.ts`; regenerate after upgrading BaGet. In the document, enums are strings
(`"Ok"`, `"SignIn"`), the error statuses `400`/`401`/`403`/`404` carry the same `ScreenResult` schema as `200`, and a screen with an optional
segment has one path per form (`/api/ui/packages/{id}` and `/api/ui/packages/{id}/{version}`).

## 4. Handle the answer: `ScreenResult<T>`

Every endpoint answers the same envelope; read the body for **every** HTTP status (errors carry it too):

| `status` | HTTP | Do |
|---|---|---|
| `Ok` | 200 | render `model` |
| `Invalid` | 400 | show `errors` (key = field name, case-insensitive; `""` = the whole form) next to the form, keep `model` |
| `SignIn` | 401 | go to the sign-in route (after one refresh attempt, see below) |
| `Forbidden` | 403 | show `errors[""]` (e.g. "administrators only", "use the web screen") |
| `NotFound` | 404 | not found, or not visible to this caller |
| `Next` | 200 | navigate to `next.screen` (`{ controller, action }`) with `next.routeValues`; show `next.message` / `next.error` once |

`next.screen` names an MVC screen; map it to your route with one table, for example
`{ "AdminUsers/Index": "/admin/users", "Package/Index": "/packages/:id/:version?" }`. A read-only answer `503` (problem details,
`Retry-After`) means the server is in maintenance.

## 5. Session handling

1. **Sign in**: `POST /api/ui/session { userName, password }`. If the answer has `twoFactorRequired`, ask for the code and post
   `{ ticket, code }` to `/session/two-factor` (or `{ ticket, recoveryCode }` to `/session/recovery-code`).
2. **Store**: keep the access token in memory; the refresh token in `sessionStorage` (one tab, gone when it closes) or in memory only
   (sign in again after a reload). Never put tokens in `localStorage`, cookies you set yourself, or URLs.
3. **Call**: add `Authorization: Bearer <access>` to `/api/ui` calls only (never to third-party hosts).
4. **Refresh**: on a `401`, call `POST /session/refresh { refreshToken }` **once** (share one refresh between concurrent calls), store
   the new pair, retry the call; if the refresh fails, sign in again.
5. **Renewed tokens**: some account actions (password change, two-factor setup) change the account's security stamp, which ends every
   older token. The answer then carries the new pair in `X-Session-Access-Token`, `X-Session-Access-Expires`,
   `X-Session-Refresh-Token`, `X-Session-Refresh-Expires`: adopt them.
6. **Sign out**: forget the tokens; `POST /session/sign-out { everywhere: true }` ends every session of the account.
7. **Confirmations**: sensitive actions take the user's password in the body, like the screens' password boxes (the setup key of
   two-factor and creating an API key ask it too, in `X-Confirm-Password` or the body).

**External sign-in (OpenID Connect).** Offer one button per provider of `GET /api/ui/session/providers` (anonymous; `model.providers: [{ id, displayName }]`, empty when none), and navigate the browser to
`/api/ui/session/external/<id>?returnUrl=<your callback>?state=<random>` after saving `state` in `sessionStorage`. BaGet redirects
back to `<your callback>?state=…#exchange_token=…`. On that route:

```ts
const params = new URLSearchParams(location.hash.slice(1));
history.replaceState(null, '', location.pathname);           // remove the token from history and Referer
if (new URLSearchParams(location.search).get('state') !== sessionStorage.getItem('oidc-state')) throw new Error('state mismatch');
const result = await post('/api/ui/session/exchange', { exchangeToken: params.get('exchange_token') });
```

The exchange token lives 60 seconds and works once; checking `state` stops an attacker from signing a victim in to the attacker's account.

## 6. React

```ts
// src/api/client.ts (openapi-fetch)
import createClient, { type Middleware } from 'openapi-fetch';
import type { paths } from './schema';

export const api = createClient<paths>({ baseUrl: import.meta.env.VITE_BAGET_URL ?? '' });

let refreshing: Promise<boolean> | null = null;
const auth: Middleware = {
  onRequest({ request }) {
    if (session.access) request.headers.set('Authorization', `Bearer ${session.access}`);
    return request;
  },
  async onResponse({ request, response }) {
    session.adoptRenewed(response.headers);                    // X-Session-* headers
    if (response.status !== 401 || request.url.includes('/api/ui/session')) return response;
    refreshing ??= session.refresh().finally(() => (refreshing = null));
    if (!(await refreshing)) return response;                  // let the page show the sign-in route
    const retry = request.clone();
    retry.headers.set('Authorization', `Bearer ${session.access}`);
    return fetch(retry);
  },
};
api.use(auth);
```

Build with `vite build --base /app/` to serve it from BaGet. See the sample for the router table, forms with `errors` and the screens.

## 7. Angular

```ts
// src/app/session/session.interceptor.ts
export const sessionInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(SessionService);
  if (!session.isApiCall(req.url)) return next(req);
  const send = () => next(session.withBearer(req)).pipe(tap(e => e instanceof HttpResponse && session.adoptRenewed(e.headers)));
  return send().pipe(
    catchError(err => err.status === 401 && !req.url.includes('/api/ui/session')
      ? session.refreshOnce().pipe(switchMap(ok => ok ? send() : throwError(() => err)))   // single-flight refresh
      : throwError(() => err)));
};
```

Register it with `provideHttpClient(withInterceptors([sessionInterceptor]))`; build with `ng build --base-href /app/`. Treat
`HttpErrorResponse.error` as the `ScreenResult` body. See the sample for the guard, the reactive-form error helper and the screens.

## 8. Host it

| Where | Steps |
|---|---|
| Own origin (CDN, static host) | build, deploy, add the origin to `Cors:AllowedOrigins` |
| Served by BaGet, beside the HTML screens | build with base `/app/`, copy the output to the server, set `Frontend:SpaPath` |
| Served by BaGet, instead of the HTML screens | as above, plus `Frontend:Mvc=false` (emails then link to the SPA) |

BaGet serves the files below `SpaBasePath` and answers `index.html` for unknown routes there (client-side routing); it never shadows
`/api`, the protocols or the screens, and refuses a base path that would.

## Security checklist

- Tokens only in memory / `sessionStorage`; the `Authorization` header only to BaGet.
- A strict Content-Security-Policy on the SPA (a token readable by script is only as safe as the page against XSS).
- List exact origins in `Cors:AllowedOrigins`; never a wildcard.
- Check `state` on external sign-in and remove the fragment at once.
- Send passwords for confirmations only when the user types them; never cache them.
