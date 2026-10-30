# Extension.BaGet.Identity.Api

User accounts, package owners, organizations and per-user API keys for
[BaGet Extension](https://pieroviano.github.io/BaGet/), as web APIs: the JSON twins (`/api/ui`) of the account and administration screens, the form models they bind
and `AddBaGetAccounts`. The screens themselves (view controllers and views) are in `Extension.BaGet.Identity.FrontEnd`.

```powershell
dotnet add package Extension.BaGet.Identity.Api
```

## Usage

```csharp
services.AddBaGetWebApplication(ConfigureBaGetApplication);
services.AddBaGetAccounts(configuration);
```

`AddBaGetAccounts` reads `Accounts:Enabled` **while registering**, so the configuration must be complete before it
runs. Disabled, it adds an empty authentication setup and only the global `ApiKey` authenticates.

| Package | Holds |
|---|---|
| `Extension.BaGet.Identity.Api` (this) | `/api/ui` twins, form models, `AddBaGetAccounts`; no view controller, no view |
| `Extension.BaGet.Api.Base` | What twins and screens share: twin bases, `?handler=` selection, `[UiApi]`, `[RepositoryPage]`, `UiApiSecurityFilter`, `RequestPrincipalResolver` |
| `Extension.BaGet.Identity.FrontEnd` | The screens: view controllers and views |
| `Extension.BaGet.Identity.Application` | Use cases, DTOs, business services (`BaGet.Identity.Application.*`; no MVC reference) |

`MaintenanceAwareSignInManager` (Application) lets a sign-in complete while the read-only `MaintenanceGate` is closed (it skips the
failed-attempt counter reset; a wrong password is still refused), so administrators can reach the Settings page.

| Setting | Value |
|---|---|
| `Accounts:Enabled` | Off by default. On: callers authenticate with the global `ApiKey` if set, otherwise their own key |
| `Accounts:AllowRegistration` | Local sign-up from the sign-in page (default `true`) |
| `Accounts:RequireApproval` | A self-registered account (also first external sign-in) cannot sign in until an administrator approves it on `/admin/users` or `/admin/users/{name}` (or `baget account approve`); promoting a user approves it too (default `false`) |
| `Accounts:CreateDefaultAdministrator` | A server with no users creates `Admin` with a default password to change at first sign-in (default `true`) |
| `Accounts:InitialAdministrators` | `;`-separated existing users promoted while there is no administrator |
| `Accounts:OpenIdConnect:Authority`, `ClientId`, `ClientSecret`, `DisplayName` | External OpenID Connect provider (e.g. Entra ID); provider id `oidc` |
| `Accounts:OpenIdConnectProviders:<id>:*` | More named OpenID Connect providers (callback `/signin-oidc-<id>`), with extra scopes and parameters, claim names and an opt-in administrator / group mapping ([configuration](https://pieroviano.github.io/BaGet/configuration/#several-providers)); restart-required |
| `Email:Smtp:*` | Optional SMTP for confirmation and password-reset mail (MailKit) |
| `Site:PublicUrl` | Required for email links; without it email is suppressed |

## Screens

Every screen is a controller action and a view, in `Extension.BaGet.Identity.FrontEnd` (the package ships **no `@page` file**). URLs and `?handler=` form posts are unchanged from the Razor Pages era; a screen is identified by its controller and default GET action (`asp-controller`/`asp-action`, `Url.Action`, `RedirectToAction`), not by a page name. The old page-model types were removed (breaking for code that referenced them).

| Folder | Role |
|---|---|
| `Controllers/UI` | The JSON twins (`/api/ui`, see [UI API](https://pieroviano.github.io/BaGet/ui-api/)). Each names its screen with `[UiApi("AdminUsers", "Index")]` (view controller without the suffix + default GET action). Account self-service and package-ownership twins are screen only (403 over HTTP), except the public profile |
| `Models/Packages` | Form models of the package manage screens (npm, Maven, Docker, scan), bound by the twins and the view controllers |
| `Extensions` | `AddBaGetAccounts` |

## Layout

One type per file, no nested types, namespace = `BaGet.Identity` + folder (`CodeLayoutTests`).

## Links

[Configuration](https://pieroviano.github.io/BaGet/configuration/) · [Source](https://github.com/pieroviano/BaGet)
