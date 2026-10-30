# BaGet.Frontend.Angular

Minimal Angular (standalone components, signals, strict TypeScript, reactive forms) single-page application for BaGet, written
against the JSON UI API (`/api/ui`, see `docs/ui-api.md`) only. It shows packages, package detail, sign-in (password, two-factor,
recovery code, external provider), register / forgot / reset password / confirm email, and one administrator screen (users).

## Prerequisites

Node 22+ (or Docker: `docker run --rm -v "$PWD:/app" -w /app node:22-alpine sh -c "npm install && npx ng build"`) and a BaGet with
accounts on (default administrator `Admin` / `Admin1234!`).

## BaGet settings

| Use | Settings |
|---|---|
| Development (`npm start`, http://localhost:4200, BaGet on another origin) | `Cors:AllowedOrigins=http://localhost:4200`. Without it the session endpoints refuse a browser from another origin (`403`). Set `Frontend:SpaUrl=http://localhost:4200` so emails link to the dev server. Point `src/environments/environment.development.ts` at your BaGet (default `http://localhost:6060`) |
| Hosted by BaGet under `/app` | `npm run build`, then `Frontend:SpaPath=<this folder>/dist/baget-frontend-angular/browser`, `Frontend:SpaBasePath=/app` (the build uses `baseHref=/app/`; `apiBaseUrl` is empty = same origin, so no CORS) |
| Emails link to this app | `Frontend:SpaLinks=true` (or `Frontend:Mvc=false`), or `Frontend:SpaUrl=<absolute address>` when the app is hosted elsewhere: `/app/confirm-email`, `/app/reset-password`, `/app/sign-in` are routes here |
| External sign-in (OIDC) | the return address `{origin}{base}sign-in/external` must be under `Frontend:SpaBasePath` (same origin) or an origin of `Cors:AllowedOrigins`. The app adds a random `state` to that address (saved in `sessionStorage`) and exchanges the token only when it comes back unchanged; each exchange token works once |

A different base path: change `baseHref` in `angular.json` (production configuration) and `Frontend:SpaBasePath` together.

## Commands

| Command | Does |
|---|---|
| `npm install` | installs (`.npmrc` sets `legacy-peer-deps`: openapi-typescript 7 still declares a TypeScript 5 peer) |
| `npm start` | dev server on 4200, development environment |
| `npm run build` | production build into `dist/` |
| `npm run gen:api` | regenerates `src/app/api/schema.d.ts` from `openapi/ui.json` |
| `npm run smoke` | `scripts/smoke.mjs` against a running BaGet: `BAGET_URL=http://localhost:6060 BAGET_USER=Admin BAGET_PASSWORD=... npm run smoke` |

## Regenerating the client

`openapi/ui.json` is the `ui` OpenAPI document. Download a fresh one with an administrator's `admin:api` key (API Keys page) or the
global `ApiKey` (anyone else gets `404`), then `npm run gen:api`:

```
curl -H "Authorization: Bearer <key>" http://localhost:6060/openapi/ui.json -o openapi/ui.json
```

## How it is built

| Piece | Where | Notes |
|---|---|---|
| Typed client | `src/app/api/api-client.ts`, `api-types.ts` | A thin wrapper over `HttpClient`: path, method, query/path values, body and answer are derived from the generated `paths` type. Chosen over openapi-fetch because the Angular interceptor, DI and RxJS pipeline stay the single HTTP stack (one place for the token and the refresh), and the wrapper is ~100 lines. Only types are generated, no runtime code |
| `ScreenResult` | `src/app/api/screens.ts` | `next.screen` (`controller/action`) + `routeValues` to router URLs (`nextToUrlTree`, `follow`), flash messages, `errors` onto reactive forms (`applyServerErrors`: case-insensitive field match, `""` and unknown fields returned as form-level messages) |
| Errors | `ApiClient` | Never throws for an HTTP error: 400/401/403/404 bodies are `ScreenResult`s and are returned as they are, other failures (503, network) become a synthetic one. Screens switch on `status` |
| Session | `src/app/session/` | `SessionService`, `sessionInterceptor`, `authGuard` |

Session behaviour: the access token is only in memory; the refresh token is in `sessionStorage` (survives F5, dies with the tab, but is
readable by any script of the origin: an XSS can steal it, which an HttpOnly cookie would prevent. For a same-origin deployment BaGet also
offers the cookie mode, `POST /api/ui/session/cookie` + `X-XSRF-TOKEN`, not used here). The interceptor adds `Authorization: Bearer` to calls
to the API only, on a `401` refreshes once (single flight, shared by concurrent requests) and retries, and takes renewed pairs from
`X-Session-Access-Token`, `X-Session-Access-Expires`, `X-Session-Refresh-Token`, `X-Session-Refresh-Expires`. A refused refresh (30-day
cap, password changed, signed out everywhere) sends the person to `/sign-in`. External sign-in: the fragment of `sign-in/external` is erased with
`history.replaceState` before the 60-second `exchange_token` is posted to `/api/ui/session/exchange`.

## Security notes

- Return addresses (`returnUrl`) are followed only when they are paths of this app (`safeLocalUrl`).
- The token is sent to the configured API origin only.
- Password and codes are never stored; a content security policy (`script-src 'self'`) on the host is the best protection of `sessionStorage`.
- The server's `Cors:AllowedOrigins` is what lets another origin call the session endpoints; keep the list exact.

## Known API friction

- The OpenAPI document types enums (`ScreenStatus`, `SearchSort`) as integers while the JSON carries names; `api-types.ts` declares `ScreenStatus` by hand.
- The optional route value `{version?}` of `packages/{id}/{version?}` is documented as a path segment `{version}` plus a query parameter; `ApiClient` fills the segment from either and drops it when empty.
- Integer fields are typed `number | string`.
