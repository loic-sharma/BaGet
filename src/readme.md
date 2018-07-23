# BaGet Source Code

These folders contain the core components of BaGet:

* `BaGet` - the app's entry point that glues other components together
* `BaGet.Core` - the core logic and services
* `BaGet.Web` - the API endpoints for BaGet
* `BaGet.UI` - BaGet's frontend

You can run BaGet on Azure using:

* `BaGet.Azure` - Azure implementation of BaGet
* `BaGet.Tools.AzureSearchImporter` - tool that populates an Azure Search index with the database's package metadata.

Lastly, BaGet comes with these tools:

* `BaGet.Tools.ImportDownloads` - imports download counts from [nuget.org](https://www.nuget.org)