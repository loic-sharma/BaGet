# Extension.BaGet.Identity.FrontEnd

The account and administration **screens** of [BaGet Extension](https://pieroviano.github.io/BaGet/): view controllers and views (sign-in, profile, API keys,
package management, administration). Each controller binds the form, calls its JSON twin (`Extension.BaGet.Identity.Api`) in process and renders.
A host that serves only the APIs does not reference it.

```powershell
dotnet add package Extension.BaGet.Identity.FrontEnd
```

Registered by `AddBaGetFrontEnd()` of `Extension.BaGet.Web.FrontEnd`, which adds this assembly as an MVC application part.

## Contents

| Folder | Role |
|---|---|
| `Controllers/FrontEnd` | The view controllers: `Account*`, `ProfileController`, `PackageManage`, `NpmManage`, `MavenManage`, `DockerManage`, `ContactOwners`, `ScanArtifact`, `ValidationStatusPage`, `Admin*` |
| `Views/<Controller>/<Action>.cshtml` | The screens; a host overrides one with its own `Views/<Controller>/<Action>.cshtml`. The layout and shared partials are in `Extension.BaGet.Frontend.Base` |
| `Models/Packages` | `ManageViewModel` (the form models the twins also bind stay in `Extension.BaGet.Identity.Api`) |
| `Extensions` | `RepositoryLinks` (`Url.RepositoryContent`) |

In a view, link with `asp-controller` / `asp-action` (and `asp-route-handler="X"` for a named handler).

## Layout

One type per file, no nested types, namespace = `BaGet.Identity` + folder (`CodeLayoutTests`).

## Links

[UI API](https://pieroviano.github.io/BaGet/ui-api/) · [Source](https://github.com/pieroviano/BaGet)
