# BaGet Source Code

These folders contain the core components of BaGet:

* `BaGet` - the app's entry point that glues everything together
* `BaGet.Core` - BaGet's core logic and services
* `BaGet.Core.Server` - the services that implement [the NuGet server APIs](https://docs.microsoft.com/en-us/nuget/api/overview) using `BaGet.Core`
* `BaGet.Protocol` - libraries to interact with [NuGet servers' APIs](https://docs.microsoft.com/en-us/nuget/api/overview)
* `BaGet.UI` - BaGet's React frontend

These folders contain database-specific components of BaGet:

* `BaGet.Database.MySql` - BaGet's MySQL database provider
* `BaGet.Database.Sqlite` - BaGet's SQLite database provider
* `BaGet.Database.SqlServer` - BaGet's Microsoft SQL Server database provider

These folders contain cloud-specific components of BaGet:

* `BaGet.AWS` - the AWS implementation of BaGet
* `BaGet.Azure` - the Azure implementation of BaGet
