# BaGet Source Code

These folders contains the source code for the BaGet service:

* `BaGet` - the API endpoints for BaGet
* `BaGet.Core` - the core logic and services
* `BaGet.Services.Mirror` - code to interact with another upstream NuGet service. Supports NuGet repository read-through caching and mirroring
* `BaGet.Tools.AzureSearchImporter` - tool that populates an Azure Search index with the database's package metadata.
* `BaGet.Tools.ImportDownloads` - tool that imports download counts from [nuget.org](https://www.nuget.org)