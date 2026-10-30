# BaGet.Frontend.React

Small React 18 + TypeScript (strict) + Vite single-page app on BaGet's [`/api/ui`](../../docs/ui-api.md). Types come from the OpenAPI document (`openapi-typescript`), calls go through `openapi-fetch` (typed paths). No UI framework.

| Screen | Route | Twin |
|---|---|---|
| Package list / search | `/` | `GET /api/ui/packages` |
| Package detail | `/packages/:id/:version` | `GET /api/ui/packages/{id}/{version}` |
| Sign in (password, two-factor or recovery code, external fragment) | `/sign-in` | `POST /api/ui/session`, `/session/two-factor`, `/session/recovery-code`, `/session/exchange` |
| Register, forgot / reset password, confirm email | `/register`, `/forgot-password`, `/reset-password`, `/confirm-email` | `/api/ui/account/*` |
| Users (admin; shows 401 SignIn / 403 Forbidden) | `/admin/users` | `GET /api/ui/admin/users` |

The email routes match `Frontend:SpaLinks` (`{SpaBasePath}/confirm-email`, `/reset-password`, `/sign-in`).

## Prerequisites

Node 20+ (or Docker with `node:22-alpine`), and a BaGet with accounts on (the default).

## BaGet settings

| Case | Settings |
|---|---|
| Dev, Vite on `http://localhost:5173` | `Cors:AllowedOrigins=http://localhost:5173` (session endpoints refuse browser origins that are not listed); `Frontend:SpaUrl=http://localhost:5173` so emails link to the dev server; `VITE_BAGET_URL=http://localhost:6060` for a direct call, or leave it empty to use the Vite proxy (`BAGET_DEV_PROXY`, default `http://localhost:6060`), which still sends `Origin: http://localhost:5173` |
| Served by BaGet | `Frontend:SpaPath=<path to dist>`, `Frontend:SpaBasePath=/app` (the build's Vite `base`); same origin, so `VITE_BAGET_URL` stays empty and no CORS entry is needed. Emails link to the app with `Frontend:SpaLinks=true` (or `Frontend:Mvc=false`). External sign-in returns must lie under the base path; the app adds a random `state` to the return address and checks it before exchanging the token (each exchange token works once). When the app is hosted elsewhere, set `Frontend:SpaUrl` to its absolute address |
| Other base path | change `base` in `vite.config.ts` and `Frontend:SpaBasePath` together |

## Commands

```
npm install
npm run dev        # http://localhost:5173
npm run build      # tsc --noEmit + vite build -> dist/
npm run lint       # tsc --noEmit
npm run smoke -- http://localhost:6060 Admin 'Admin1234!' [origin]   # sign in + list packages, no browser
```

Without Node: `docker run --rm -v "$PWD:/app" -w /app node:22-alpine sh -c "npm install && npm run build"`.

## Regenerating the client

`npm run gen:api` rewrites `src/api/schema.d.ts` from `openapi/ui.json`. To refresh the document:

```
curl -H "Authorization: Bearer <key>" http://localhost:6060/openapi/ui.json -o openapi/ui.json
```

`<key>` is an administrator's API key with the `admin:api` scope and no repository limit, or the global `ApiKey`; anyone else gets 404. Session tokens do not work there.

## Notes on the code

- `src/api/client.ts`: the `openapi-fetch` middleware adds `Authorization: Bearer <access>`, adopts renewed `X-Session-*` headers, and on a 401 refreshes once (shared promise) and replays the request.
- `src/api/screen.ts`: `toScreen` normalises a `ScreenResult` (only 200 is typed in the OpenAPI document; 400/401/403/404 bodies arrive in `error`) and `nextRoute` uses `src/routes.ts` to map `next.screen` `{controller, action}` to a route.
- `src/components/Form.tsx`: field errors and the `""` form-level errors.
- Known mismatch: the document types `status` as an integer but the server sends the name (`"Ok"`); `toScreen` accepts both.

## Token storage

| Token | Where | Risk |
|---|---|---|
| Access (15 min) | memory | readable by script injected into the page, gone on reload |
| Refresh (14 days) | `sessionStorage` | survives reloads of the tab, cleared when it closes, readable by XSS |

Never `localStorage` (shared across tabs, survives restarts) and never a URL (only the 60-second external exchange token travels in a fragment, removed with `history.replaceState` at once). Sign out only forgets the tokens; "Sign out everywhere" changes the account's security stamp and ends every session. For stronger protection serve the app from BaGet's origin and use the cookie mode, or put a backend-for-frontend in front. Escape or sanitise any HTML from the API (`readmeHtml`) before injecting it.
