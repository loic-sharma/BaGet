# npm registry

A local **npm** repository is an npm registry: `npm publish`, `npm install`, dist-tags, deprecate, unpublish, search, `npm login`.
A **remote** npm repository proxies an upstream registry such as registry.npmjs.org ([details](remote-repositories.md#npm-remote-repositories)); a **virtual** one merges several npm repositories ([details](virtual-repositories.md#npm-virtual-repositories)).

## Create and use

| Step | How |
|---|---|
| Create | Administrators: `/admin/repositories` > *npm* form (a name; *Anonymous read* chosen there). *New remote repository* / *New virtual repository* with the format *npm* for the other kinds. Refused on Azure Table / Azure Search (default repository only) |
| Registry address | `https://host/r/{name}/npm/` (shown on the repository list and the browse page). `/npm/` of the root is never served: `default` is NuGet |
| Point npm at it | `npm config set registry https://host/r/{name}/npm/`, or only a scope: `@acme:registry=https://host/r/{name}/npm/` in `.npmrc` |
| Sign in | `npm login --registry=…` (user name and password, accounts on) or `//host/r/{name}/npm/:_authToken=<API key>` in `.npmrc` (any user API key, or the global `ApiKey`) |
| Publish / install | `npm publish`, `npm install pkg`, `npm install @scope/pkg@tag`, `npm view`, `npm search`, `npm ping`, `npm whoami`, `npm logout` |

`npm login` calls `POST /-/v1/login` (web login, answered 404 so npm falls back) and then
`PUT /-/user/org.couchdb.user:NAME` with the password. The answer is a token: **an API key of the user, limited to this
repository**, named `npm login (name)`, no expiry; it appears on *Account > API keys* and `npm logout` (or *Revoke*) deletes it.
Refused: wrong password, a locked-out account (429), an unconfirmed email, an account with two-factor authentication (use an
API key from the account page instead). Without accounts (`Accounts:Enabled` off) login answers 501: use the global key as the token.
Login never creates an account. On a **virtual** repository the token is limited to that repository plus its members and deploy repository (so it reads through it and pushes to the deploy repository); logout and login are never moved to the deploy repository. The logout request names the token in its path; the request log and traces show `Redacted` in its place.

## Pages

| Page | Address (also under `/r/{name}/`) | Who |
|---|---|---|
| Browse and search | `/npm-packages?q=` | whoever may read the repository; lists only packages the visitor may read |
| Package | `/npm-packages/{name}?version=` (scoped: `/npm-packages/@scope/name`) | readers of the package: readme, versions, dist-tags, dependencies, `npm install` / `yarn` / `pnpm` commands, `.npmrc` lines, tarball link, owners |
| Upload | `/npm-upload` | a signed-in user with Deploy (accounts on), or the API key typed into the form (accounts off) |
| Manage | `/npm-manage/{name}` | owners and administrators: owners, dist-tags, deprecate / remove deprecation, unpublish a version or the package |

The front page of an npm repository (`/r/{name}/`, the header, the repository switcher) leads to *Browse*. The NuGet pages
(`/packages`, `/upload`, `/statistics`) are 404 there, and npm pages are 404 in a NuGet repository.
*Unpublish* asks for the password again. Writes are refused (503) while a database copy has the maintenance gate closed, and in
read-only mode.

## Rules

| Topic | Rule |
|---|---|
| Names | `left-pad` or `@scope/name`, lower case (to publish; reading, and proxying a remote repository, also accept npm's legacy upper case names such as `JSONStream`), at most 214 characters, `a-z 0-9 . _ ~ -`, not starting with `.`, `_` or `-`; `node_modules` and `favicon.ico` reserved. An encoded `@scope%2fname` and `@scope/name` are the same package |
| Versions | semantic versions only (`1.0.0`, `1.0.0-rc.1`, `1.0.0+build`); build metadata does not make a version different, nor does the case of the prerelease label (`1.0.0-Beta` is `1.0.0-beta`) |
| Publish | `PUT /{name}`: the body carries the tarball as base64. It is streamed to a temporary file and decoded to another, never held in memory. Refused (400, nothing stored) when the declared `integrity` (sha512), `shasum` (sha1) or `length` differ from the bytes, when the tarball has no `package.json` or it names another package or version, when the tag is missing or looks like a version. `409` for a published version (publishes of one version are serialized per process, so a loser never replaces the winner's file). The install fields (`dependencies`, `bin`, `os`, `cpu`, `engines`, `scripts`-derived `hasInstallScript`...) are taken from the tarball's own `package.json`, not from the request's manifest. Exception: a `scripts.install` of exactly `node-gyp rebuild` (with `gypfile`) and a `bin` for a tarball that declares only `directories.bin` are kept from the request, as npm adds them when packing. Size limit `Npm:MaxPublishBytes` (413); the document apart from the tarballs (manifest, readme) is held in memory and limited to 64 MiB (413) |
| First version | always gets the `latest` tag, whatever tag it was published with. Later publishes move only the tag they name |
| Ownership | the first publisher of a name owns it (organizations can own); only owners (and administrators) publish versions, tag, deprecate, unpublish. An administrator's push does not take the name over. A name published before accounts has no owner: only the global key and administrators change it |
| Access model | Read for browsing, installing, searching; **Deploy** to publish and to change dist-tags; **Annotate** to deprecate; **Delete** to unpublish after the window. Package patterns of grants apply to the decoded name. API key scopes: `package:push` publishes new names, `package:pushversion` only new versions of owned names, `package:unlist` deprecates and unpublishes after the window |
| Dist-tags | `latest` stays pointed somewhere: removing a version re-points tags with the client's list, `latest` falls back to the highest stable version. A tag cannot be a version |
| Deprecate | `npm deprecate pkg@range "message"` sets the message of each version; an empty message clears it |
| Unpublish | see below |
| Search | `/-/v1/search` and the page match the package **name**; a listing never names a package its caller cannot read |
| Audit | `npm audit` / the audit step of `npm install` get "no advisories" (findings are shown on the package pages and enforced on download/publish, see [vulnerability scanning](vulnerability-scanning.md); the audit endpoints stay empty) |
| Writes need an API key | a Bearer token, Basic password or `X-NuGet-ApiKey`; never the browser session cookie |

### Unpublish policy

Same shape as npmjs, with the permission model of this server:

| Case | Needs |
|---|---|
| A version published less than `Npm:UnpublishWindow` ago (default **72 hours**), or a package whose every version is that young | the owner's **Deploy** and a key with `package:push` (a key limited to `package:pushversion` never deletes: it needs `package:unlist` and Delete like an old version) |
| Anything older | **Delete** on the package, ownership (or administrator) and `package:unlist` |
| `UnpublishWindow` zero or negative | always Delete |

The tarball is deleted with the version; the number can be published again (npmjs forbids that for ever; here a deleted version
is deleted, as in the NuGet feed). Unpublishing the last version removes the package (its owners are kept, like a NuGet id's).
`npm unpublish pkg@1.0.0` is `PUT /{name}/-rev/{rev}` (the packument without the version) then `DELETE /{name}/-/{file}/-rev/{rev}`;
`npm unpublish pkg --force` is `DELETE /{name}/-rev/{rev}`. The revision is not checked.

## Routes (below `/r/{name}/npm/`)

| Route | Does |
|---|---|
| `GET {name}` | packument (`Accept: application/vnd.npm.install-v1+json` gets the install-only form) |
| `GET {name}/-/{file}` | tarball (ETag = integrity) |
| `PUT {name}` | publish (with `_attachments`) or deprecate (a packument without attachments; only `deprecated` can change) |
| `PUT\|DELETE {name}/-rev/{rev}`, `DELETE {name}/-/{file}/-rev/{rev}` | unpublish steps |
| `GET -/package/{name}/dist-tags`, `PUT\|DELETE …/dist-tags/{tag}` | dist-tags (body of PUT: the version as a JSON string) |
| `GET -/v1/search`, `-/ping`, `-/whoami` | search, ping, user name of the token |
| `POST -/v1/login` (404), `PUT -/user/org.couchdb.user:{user}`, `DELETE -/user/token/{token}` | login, logout |
| `POST -/npm/v1/security/advisories/bulk`, `…/audits/quick` | no advisories |

A scoped name is also accepted as two segments (`@scope/name`) on every route that names a package. Every route is a 404 in a repository that is not npm. Names are case-sensitive on every database (legacy names such as `JSONStream` differ from `jsonstream`). In a private repository a caller without Read gets `404 Not found` from every change route, whether the package exists or not.

## Configuration

| Key | Default | Meaning |
|---|---|---|
| `Npm:UnpublishWindow` | `72:00:00` | see above. Live |
| `Npm:MaxPublishBytes` | `536870912` (512 MiB) | largest publish request body (a tarball is about 3/4 of it); `0` = no limit beyond the web server's. Live |

The web server's own body limit applies as well (the shipped host turns Kestrel's `MaxRequestBodySize` off; IIS has its own caps, see [formats](formats.md#streaming-storage-b2)).

## Storage and data

Tarballs: `npm/{name}/-/{file}` below the repository's storage prefix (`repos/{name}/` for repositories other than `default`).
Tables `NpmPackages`, `NpmVersions`, `NpmDistTags` ([formats](formats.md#data-model-b1)); the manifest of each version is kept as
published (without the tarball address, which is built from the request), except the install fields, which come from the tarball's `package.json`, so the packument cannot promise what `npm install` will not run.

## Tests

`BaGet.Clients.Tests` runs the real npm 10 in `node:22-alpine` against the real application on a socket (needs Docker;
skipped otherwise): publish, install, scoped names through `%2f`, dist-tag, deprecate, unpublish, search, ping, whoami, audit,
login (npm's own login library; `npm login` needs a terminal), logout, a private repository, a 40 MB tarball with bounded
server allocations. The protocol, access, pages and parser are covered in `BaGet.Tests` (`NpmRegistryTests`,
`NpmAccessTests`, `NpmPagesTests`) and `BaGet.Core.Tests` (`NpmPublishBodyTests`).

## Vulnerability scanning

With `Scanning:Enabled`, each stored version and its dependencies that name one exact version are checked against OSV. A
policy of the repository can refuse the tarball download (`403 {"error": reason}`) or the publish (`403`, nothing stored); the
packument stays readable. See [vulnerability scanning](vulnerability-scanning.md). Tests: `ScanningNpmTests`.
