# Extension.BaGet.Frontend.Base

The MVC base of the screens of [BaGet Extension](https://pieroviano.github.io/BaGet/): the view controller bases, the layout and shared partials, and the link helpers.
It has no controller of its own. The screens are in `Extension.BaGet.Identity.FrontEnd` and `Extension.BaGet.Web.FrontEnd`, which reference this package;
the `/api/ui` twin bases, `[UiApi]`, `[RepositoryPage]`, `ScreenEndpoints` and `RequestPrincipalResolver` are in `Extension.BaGet.Api.Base`, which this package references.

```powershell
dotnet add package Extension.BaGet.Frontend.Base
```

Reference it directly only to write your own screens on the same bases; the business services are in
`Extension.BaGet.Identity.Application` (namespace `BaGet.Identity.Application.*`).

## Contents

| Folder / type | Role |
|---|---|
| `Controllers/Base/UiControllerBase` (an `IScreenController`) and `AccountControllerBase`, `AdminControllerBase`, `AccessControllerBase`, `RepositoryControllerBase`, `RetentionControllerBase`, `ReplicationControllerBase` | Bases of the view controllers: antiforgery on every POST (`[AutoValidateAntiforgeryToken]`) and the shared checks (`ScreenGate`: admin or project admin, `NullContext` redirect, maintenance gate on POST, password confirmation) in a fixed order. `AdminControllerBase.Flash(model)` carries `Message`/`Error` across a redirect |
| `Mvc/Attributes/ImplicitPageHandlerAttribute` | The default POST that renders the screen (a form posted to its own URL); not in the route contract |
| `Mvc/Screens` | `ScreenOutcome`, `AccessSupport`, `PackageControllerSupport`, `AccountScreen`: shared screen rules |
| `Mvc/UI/UiApiInvoker` | A view controller calls its twin in process |
| `Routing` | `RepositoryUrlHelper` / `RepositoryUrlHelperFactory` (links stay inside the repository being browsed), `UiActionMap` |
| `TagHelpers/NavLinkTagHelper` | `nav-link`: marks the anchor of the current screen |
| `Views/Shared` | The layout (`_Layout`) and the partials both front ends use; BaGet's views name them (`Layout = "_Layout"`, `<partial name="_Avatar">`), so a host copy in its own `Views/Shared` or `Pages/Shared` overrides them |

## Layout

One type per file, no nested types, namespace = `BaGet.Frontend` + folder (`CodeLayoutTests`).

## Links

[UI API](https://pieroviano.github.io/BaGet/ui-api/) · [Source](https://github.com/pieroviano/BaGet)
