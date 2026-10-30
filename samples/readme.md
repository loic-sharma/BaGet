# Samples

Here you can find samples that show you how to use BaGet in your own projects:

* `BaGetWebApplication` - Embed BaGet into your own ASP.NET Core project. `AddBaGetWebApplication` brings the V3 API, web UI, catalog, the validation pipeline (off unless `Validation:Enabled`) and browser-upload staging; the sample adds SQLite and file storage. It calls `UseRepositoryResolution()` (required: `/r/{repo}/` is answered 500 without it) and serves the UI files of `src/BaGet/wwwroot` at `/_content/BaGet.Web`. Accounts are not wired: the full host, `src/BaGet/Program.cs`, adds them
* `BaGet.Protocol.Samples.Tests` - Interact with a NuGet V3 API
