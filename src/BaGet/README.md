# Extension.BaGet

The host of [BaGet Extension](https://pieroviano.github.io/BaGet/), a lightweight NuGet, npm, Maven and Docker registry and symbol server. It
wires every other package together and serves the [NuGet service APIs](https://learn.microsoft.com/nuget/api/overview);
most of the logic lives in `Extension.BaGet.Core`.

```powershell
dotnet add package Extension.BaGet
```

## Contents

| Item | Use |
|---|---|
| `Program` | Minimal-hosting entry point and CLI; registers every database, storage and search provider |
| `wwwroot/` | `Extension.BaGet.Web`'s static files, for hosts without the Web SDK's static web assets: serve them from `wwwroot/_content/BaGet.Web` |

## Running

Without arguments it applies database migrations and starts the server on `http://localhost:6060`
(`Kestrel:Endpoints:Http:Url` in `appsettings.json`; override with `Kestrel__Endpoints__Http__Url`; `--urls` is ignored).

| Command | Does |
|---|---|
| `import downloads [-r <repo>]` | Imports nuget.org's download counts for the packages of a repository (default `default`) |
| `catalog rebuild [-r <repo>]` | Appends the catalog entries missing for the packages of a repository (default `default`), for example packages pushed before the catalog existed |
| `account reset-password <user> [--email <address>]` | Sets a generated password, switches off two-factor and lifts lockout; `--email` sets and confirms an address. Needs `Accounts:Enabled` |
| `account promote <user>` / `account demote <user>` | Makes a user an administrator (also approves a pending account) / removes the flag (the last administrator cannot be demoted). Needs `Accounts:Enabled` |
| `account approve <user>` | Enables an account waiting for approval (`Accounts:RequireApproval`). Needs `Accounts:Enabled` |

Settings saved from the UI go to `appsettings.Overrides.json` (path: `SettingsOverridesPath`), applied after
`appsettings*.json` and before environment variables and the command line.

## Links

[Documentation](https://pieroviano.github.io/BaGet/) · [Configuration](https://pieroviano.github.io/BaGet/configuration/) · [Source](https://github.com/pieroviano/BaGet)
