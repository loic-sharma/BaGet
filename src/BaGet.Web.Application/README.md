# Extension.BaGet.Web.Application

The web **use cases** of [BaGet Extension](https://pieroviano.github.io/BaGet/): the data of the browse, upload and legal screens as plain DTOs
(`StatisticsViewModel`, `LegalScreen`, `IndexScreen`, `PackageScreen`, `ReportAbuseScreen`, the npm, Maven, Docker and upload view models, ...), their use cases (`StatisticsUseCase`, `LegalUseCase`, `IndexUseCase`, `PackageUseCase`, `ReportAbuseUseCase`, `UploadUseCase`, `NpmBrowseUseCase`, `MavenBrowseUseCase`, `DockerBrowseUseCase`, ...) and the helpers that compute presentation
data (readme Markdown to safe HTML, install snippets, dependency and framework grouping). A browser upload takes its file as an HTTP-free `UploadedFile` and authenticates through `UploadAuthenticator` (the session, a pasted API key while accounts are off, or the key of a JSON API request). No MVC or Razor dependency (test-enforced); the `/api/ui` controllers that expose these use cases as JSON are in
[`Extension.BaGet.Web.V3`](https://www.nuget.org/packages/Extension.BaGet.Web.V3), the screens in [`Extension.BaGet.Web.FrontEnd`](https://www.nuget.org/packages/Extension.BaGet.Web.FrontEnd).

A use case takes its inputs explicitly, returns a `ScreenResult<T>` (see `Extension.BaGet.Identity.Application`) and never sees `HttpContext`, `ModelState`
or `TempData`. `AddBaGetWebUseCases` registers them; `AddBaGetWebApplication` calls it.

## Layout

One type per file, namespace = `BaGet.Web.Application` + folder (`CodeLayoutTests`): `UseCases`, `Screens`, `Snippets`, `Services`, `Models/{Browse,Packages,Npm,Maven,Docker,Upload}`, `Extensions`.
