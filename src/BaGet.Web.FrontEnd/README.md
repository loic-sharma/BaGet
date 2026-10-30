# Extension.BaGet.Web.FrontEnd

The package-browsing **screens** of [BaGet Extension](https://pieroviano.github.io/BaGet/) and the registration of the whole web UI: view controllers and views of the home,
package, statistics, report abuse, upload, legal, API catalog and npm / Maven / Docker browse pages. Each controller calls its JSON twin
(`Extension.BaGet.Web.V3`) in process and renders. A host that serves only the APIs does not reference it.

```powershell
dotnet add package Extension.BaGet.Web.FrontEnd
```

## Usage

```csharp
builder.Services.AddBaGetWebApplication(ConfigureBaGetApplication);   // Extension.BaGet.Web.V3: APIs and protocols
builder.Services.AddBaGetFrontEnd();                                  // this package: screens, layout, link helpers
```

`AddBaGetFrontEnd` adds `Extension.BaGet.Frontend.Base`, `Extension.BaGet.Identity.FrontEnd` and this assembly as MVC application parts (a host without the
Web SDK has no generated `[ApplicationPart]` attributes), keeps Razor Pages registered for a host's own pages, and replaces `IUrlHelperFactory` with `RepositoryUrlHelperFactory`
(links stay inside the repository being browsed).

## Contents

| Folder | Role |
|---|---|
| `Controllers/FrontEnd` | `HomeController`, `PackageController`, `StatisticsController`, `ReportAbuseController`, `ApiCatalogController`, `LegalController`, `UploadController`, `NpmBrowseController`, `MavenBrowseController`, `DockerBrowseController` |
| `Views` | One folder per controller; `Views/Shared/_ScanFindings`. The layout and the other shared partials are in `Extension.BaGet.Frontend.Base` |
| `ViewModels` | View models only the screens use (the API catalog's are in `Extension.BaGet.Web.V3`) |
| `Extensions` | `AddBaGetFrontEnd`, `RazorExtensions` |

## Layout

One type per file, no nested types, namespace = `BaGet.Web.V3` + folder (`CodeLayoutTests`; `FrontEndServiceCollectionExtensions` stays in `BaGet`).

## Links

[Documentation](https://pieroviano.github.io/BaGet/) · [UI API](https://pieroviano.github.io/BaGet/ui-api/) · [Source](https://github.com/pieroviano/BaGet)
