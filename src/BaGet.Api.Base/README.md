# Extension.BaGet.Api.Base

The base of the web APIs of [BaGet Extension](https://pieroviano.github.io/BaGet/): the `/api/ui` JSON twin bases and what both the APIs and the
screens need. No view controller, no view. `Extension.BaGet.Identity.Api` and `Extension.BaGet.Web.V3` reference it; so does `Extension.BaGet.Frontend.Base`.

```powershell
dotnet add package Extension.BaGet.Api.Base
```

## Contents

| Folder / type | Role |
|---|---|
| `Controllers/Base/ApiControllerBase` and `AccountApiControllerBase`, `AdminApiControllerBase`, `AccessApiControllerBase` | Bases of the JSON twins (`/api/ui`, see [UI API](https://pieroviano.github.io/BaGet/ui-api/)); visible in the OpenAPI `ui` document |
| `Mvc/Attributes` | `PageHandlerAttribute` (`?handler=` selector), `FallbackHandlerAttribute`, `RepositoryPageAttribute` (`/r/{repo}/` twin routes and the repository check), `RequiresSignInAttribute`, `RequiresEmailAttribute` |
| `Mvc/UI/UiApiAttribute` | `[UiApi("AdminUsers", "Index", Handler = ...)]` names the screen (controller without the suffix, default GET action) a twin mirrors; `ScreenCatalogTests` resolves the names against the front-end assemblies |
| `Mvc/UI/IScreenController`, `ScreenEndpoints` | The marker `UiControllerBase` implements, and whether an action is a screen and which one (`ScreenRef`) |
| `Mvc/UI` (`UiApiSecurityFilter`, `UiApiCaller`, `UiApiEntry`, `UiApiResult`, `UiApiResponse`) | `/api/ui` over HTTP: API keys only (Bearer or `X-NuGet-ApiKey`); no cookie, no Basic, no antiforgery |
| `Mvc/Screens` (`ScreenLinks`, `AccountSupport`) | Absolute screen links for emails, account re-authentication rules |
| `Security` | `RequestPrincipalResolver`, `BasicCredentials`, `HttpReauthenticationState` |

## Layout

One type per file, no nested types, namespace = `BaGet.Frontend` + folder (`CodeLayoutTests`; the namespace the types had in `Extension.BaGet.Frontend.Base`).

## Links

[UI API](https://pieroviano.github.io/BaGet/ui-api/) · [Source](https://github.com/pieroviano/BaGet)
