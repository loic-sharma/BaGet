# Testing the APIs with Scalar

BaGet serves its OpenAPI documents with a [Scalar](https://scalar.com) viewer at **`/scalar`**: browse every endpoint, read its
schema and send real requests from the browser.

## Who can open it

Administrators only; everyone else (anonymous included) gets `404`, for the viewer and for the JSON.

| Caller | Opens `/scalar` and `/openapi/*.json` |
|---|---|
| Signed-in server administrator (browser session) | yes |
| An administrator's API key with the `admin:api` scope, not limited to repositories | yes |
| The server's global `ApiKey` (only when one is configured) | yes |
| Anyone else, or accounts disabled without a global `ApiKey` | no (`404`) |

The simplest way: sign in as an administrator, then open `https://<server>/scalar` (or follow the link on **Admin > REST API**, `/admin/api`).

## The three documents

Pick one with the document selector at the top of the viewer.

| Document | JSON | What it covers | Authenticate with |
|---|---|---|---|
| `ui` | `/openapi/ui.json` | `/api/ui/...`: the JSON twin of every web screen (what an Angular/React frontend calls) | an API key (`bearer` or `apiKey`) or a session token (`session`); never the bare cookie, never Basic |
| `v1` | `/openapi/v1.json` (alias `/api/v1/openapi.json`) | `/api/v1/...`: the administration REST API ([admin-api.md](admin-api.md)) | an administrator's `admin:api` key (never the session) |
| `protocols` | `/openapi/protocols.json` | NuGet V3/V2, symbols, npm, Maven and Docker (OCI) routes, at `/` and `/r/{repo}/` | anonymous where the repository allows it, else an API key |

## Getting an API key

Signed in, open **Account > API keys** (`/account/apikeys`) and create a key. For `/api/v1` choose an administrator account and the
`admin:api` scope, with no repository limit. The key is shown once: copy it.

## Setting the credentials in Scalar

Open the **Authentication** box of the viewer (or the *Auth* tab of the request window) and choose one scheme:

| Scheme | Fill in | Use for |
|---|---|---|
| `bearer` | the key | `/api/v1`, `/api/ui`, most protocol routes |
| `session` | the `accessToken` of `POST /api/ui/session` | `/api/ui` only (a person's session) |
| `basic` | any user name (npm/Maven send the account name), the key as password | `/api/v1` and the protocol routes (what Maven and Docker clients send); **not** `/api/ui` |
| `apiKey` (`X-NuGet-ApiKey` header) | the key | `/api/ui`, NuGet push/delete, also accepted elsewhere |

A wrong or revoked key is refused with `401`; it never falls back to the session (no document accepts the session cookie). The `ui` document offers `bearer`, `apiKey` and `session`.

**Shape of the `ui` document** (for generated clients): enums are string schemas listing their names (`ScreenStatus`: `Ok`, `Invalid`, ...), as the API writes them; integers are plain `integer`; every operation that answers a `ScreenResult<T>` describes its `400`, `401`, `403` and `404` answers with the schema of its `200`; an optional route value is two paths (`/api/ui/packages/{id}` and `/api/ui/packages/{id}/{version}`, the value a required path parameter in the second), never a query parameter behind a path that names it.

## Testing `/api/ui` (screens as JSON)

Set the `bearer` (or `apiKey`) scheme to a key, as for `/api/v1`, or the `session` scheme to the `accessToken` of `POST /api/ui/session`. The browser cookie is **not**
used here unless a request also carries the antiforgery token of `GET /api/ui/antiforgery` in `X-XSRF-TOKEN`. Without a credential you are anonymous: public screens work (home, package, statistics, report abuse), a
protected one answers `401` with a message saying to send a key; HTTP Basic is refused with `401`.

- **Administration screens** (`/api/ui/admin/...`) need an administrator's session token or key with the `admin:api` scope and no repository limit, e.g. `GET /api/ui/admin/repositories` returns
  the model the **Repositories** screen renders. The server's global `ApiKey` is refused with `403` (it has no owner): use an administrator's key.
- **Account self-service** (sign-in, password, two-factor, API keys, organizations, deletion) and **package ownership** screens (manage, contact owners, validation status, scan)
  need an interactive session (a session token): they appear in the document, marked "Interactive session only", and answer `403` "use the web screen" to an API key and to an anonymous caller.
- A key limited to repositories sees other repositories as `404`.

**Confirmation passwords.** Actions whose screen asks your password (delete, purge, access changes, ...) take it in the request body field
the schema shows (e.g. `password`). A wrong one counts as a failed sign-in and can lock the account.

**Reading the answer.** Every `/api/ui` response is a screen result:

| Field | Meaning |
|---|---|
| `status` | `Ok`, `Invalid`, `NotFound`, `Forbidden`, `SignIn`, `Next` |
| `model` | the data the screen renders (present also when `Invalid`, so a form can be shown again) |
| `errors` | field name -> messages (`""` = the whole form) |
| `next` | after a write: where to go (`screen` = `{ controller, action }` such as `AdminUsers` / `Index`, `routeValues`, or a validated `localUrl`; `fragment`) and the flash `message` or `error` |
| `statusCode` | set when the screen itself answers with a specific code |

HTTP status: `200` (Ok, Next), `400` (Invalid), `401` (SignIn), `403` (Forbidden), `404` (NotFound), `503` while the server is read-only
(maintenance, database copy); some screens answer with their own code (e.g. `409`).

## Testing `/api/v1` (administration)

Set the `bearer` scheme to an administrator's `admin:api` key (or the global `ApiKey`, which `/api/v1` still accepts); the session is never accepted here. Operations
marked as needing confirmation also take the key owner's password in the **`X-Confirm-Password`** header (the global key needs none).
Errors are `application/problem+json` with a stable `code`; lists are paged (`page`, `pageSize` up to 200). Details: [admin-api.md](admin-api.md).

## Testing the package protocols

Good for reads: service index (`GET /v3/index.json`), search, package metadata, npm packuments, Maven metadata, OCI tags and manifests.
For a private repository authenticate with a key (`basic` with the key as password, or `bearer`). For OCI (`/v2/...`) an anonymous call
returns `401` with a `Bearer realm=.../v2/token` challenge: call `GET /v2/token?scope=repository:<repo>/<image>:pull` with Basic
credentials and use the returned token as `bearer`.

Uploads (a `.nupkg`, an npm tarball, Docker layers) are binary or multi-step and are easier with the real clients (`dotnet nuget push`,
`npm publish`, `mvn deploy`, `docker push`); the document describes their routes and parameters, not their bodies.

## Tips

- Test against a copy or a test repository: Scalar sends real requests, deletes included.
- The read-only gate (Settings, database copy) makes every write answer `503` with `Retry-After`.
- The documents never contain keys or secrets; download them (`/openapi/<name>.json`) to generate a client (e.g. `openapi-generator`,
  NSwag, `orval` for React/Angular).
