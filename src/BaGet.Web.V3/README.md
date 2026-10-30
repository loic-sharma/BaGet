# Extension.BaGet.Web

The NuGet V3 server endpoints and the web APIs of [BaGet Extension](https://pieroviano.github.io/BaGet/). Use it
to embed BaGet in your own ASP.NET Core host.

```powershell
dotnet add package Extension.BaGet.Web
```

## Usage

```csharp
builder.Services.AddBaGetWebApplication(app =>
{
    app.AddSqliteDatabase();   // Extension.BaGet.Database.Sqlite
    app.AddFileStorage();
});
builder.Services.AddBaGetAccounts(builder.Configuration); // Extension.BaGet.Identity.Api
builder.Services.AddBaGetFrontEnd();                       // Extension.BaGet.Web.FrontEnd: the screens (omit it to serve the APIs only)

// Pick each subsystem from configuration (Database:Type, Storage:Type, Search:Type).
builder.Services.AddScoped(DependencyInjectionExtensions.GetServiceFromProviders<IContext>);
builder.Services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IStorageService>);
builder.Services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<IPackageDatabase>);
builder.Services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchService>);
builder.Services.AddTransient(DependencyInjectionExtensions.GetServiceFromProviders<ISearchIndexer>);

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.UseRepositoryResolution();     // /r/{repo}/ selects the repository (404 if unknown); required: without it a /r/{repo}/ request is answered 500 (RepositoryResolutionGuardFilter), never served from the default repository
app.UseAuthentication();
app.UseAuthorization();
app.UseOperationCancelledMiddleware();
app.UseMaintenanceMiddleware();   // 503 + Retry-After for writes refused by a closed MaintenanceGate
new BaGetEndpointBuilder().MapEndpoints(app);
```

Run migrations with `host.RunMigrationsAsync()` (honours `RunMigrationsAtStartup`). Hosts on the generic host can call
`hostBuilder.UseBaGet(configure)` instead of `AddBaGetWebApplication`. The complete reference host is
`Program.BuildApplication` in `Extension.BaGet` (`AddBaGetHost` / `UseBaGetPipeline`, under `src/BaGet/Hosting`).

| Item | Role |
|---|---|
| `AddBaGetWebApplication` | MVC controllers (the screens come with `AddBaGetFrontEnd`), `IUrlGenerator`, core services, catalog, validation; registers every BaGet API controller assembly as an application part, so hosts without the Web SDK work |
| `BaGetEndpointBuilder.MapEndpoints` | Maps the V3, V2, catalog and validation routes, each at `/` (default repository) and `/r/{repo}/` |
| `RepositoryActionRouteConvention` / `RepositoryActionFilter` | The controller actions marked `[RepositoryPage]` (list, package, statistics, upload, ...): the `/r/{repo}/` twin of every route, and the one repository check (`RepositoryPageGate`, which asks `IRepositoryAccess`) |
| Screens | Not here: the view controllers and views (`HomeController`, `PackageController`, `UploadController`, the browse controllers, ...) are in `Extension.BaGet.Web.FrontEnd`, the layout and link helpers (`RepositoryUrlHelperFactory`) in `Extension.BaGet.Frontend.Base` |
| `Controllers/UI` | The JSON twins of the screens, `GET /api/ui/...` (and `/api/ui/r/{repo}/...`): `[UiApi("Statistics", "Index")]` actions on `ApiControllerBase` that call a use case of `Extension.BaGet.Web.Application` and answer a `ScreenResult<T>`. The view controller calls its twin in process and only renders the answer. Every browse, upload and legal screen has one: `LegalApiController`, `StatisticsApiController`, the browse twins (`home`/`packages`, package, report abuse, API catalog), `UploadApiController` (multipart stage, JSON submit/cancel), `NpmBrowseApiController`, `MavenBrowseApiController` and `DockerBrowseApiController`. A shown screen that carries its own status (an upload refused with 403, a package page that explains a missing package with 404) is answered with it. Authentication: an API key (`Authorization: Bearer` or `X-NuGet-ApiKey`) or anonymous; never the cookie, Basic or antiforgery; see [UI API](https://pieroviano.github.io/BaGet/ui-api/) |
| `RepositoryResolutionMiddleware` | Turns the `{repo}` route value into `IRepositoryContext`; unknown repository: `404`, never the default |
| `MaintenanceMiddleware` | Maps `MaintenanceException` (a write refused while `MaintenanceGate` in `Extension.BaGet.Core` is closed, e.g. during a database copy) to `503` + `Retry-After` |
| `IUrlGenerator` | Builds every absolute URL the server returns |
| Static files | `wwwroot` is in the host (`Extension.BaGet.Site`), served at `/` and at `_content/BaGet.Web` |

## Layout

One type per file, no nested types, namespace = `BaGet.Web.V3` + folder (`CodeLayoutTests`; `BaGetEndpointBuilder` and `IServiceCollectionExtensions` stay in `BaGet`). `Controllers/UI` JSON twins, `Controllers/Api` `/api/v1` administration REST, `Controllers/Protocols` package protocols (NuGet, npm, Maven, OCI), `Controllers/Base` bases; `Api` (catalog, filters), `Models/{Api,Protocols}`, `Routing`, `Filters`, `Middleware`, `Oci`, `OpenApi`, `ViewModels` (the API catalog's), `Workers`, `Extensions`.

## Links

[Documentation](https://pieroviano.github.io/BaGet/) · [Configuration](https://pieroviano.github.io/BaGet/configuration/) · [Server API](https://pieroviano.github.io/BaGet/advanced/api/) · [Source](https://github.com/pieroviano/BaGet)
