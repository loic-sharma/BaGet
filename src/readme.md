# BaGet Source Code

These folders contain the core components of BaGet:

* `BaGet` - The app's entry point that glues everything together.
* `BaGet.Core` - BaGet's core logic and services.
* `BaGet.Web` - The [NuGet server APIs](https://docs.microsoft.com/en-us/nuget/api/overview) and web UI.
* `BaGet.Protocol` - Libraries to interact with [NuGet servers' APIs](https://docs.microsoft.com/en-us/nuget/api/overview).

These folders contain database-specific components of BaGet:

* `BaGet.Database.MySql` - BaGet's MySQL database provider.
* `BaGet.Database.PostgreSql` - BaGet's PostgreSql database provider.
* `BaGet.Database.Sqlite` - BaGet's SQLite database provider.
* `BaGet.Database.SqlServer` - BaGet's Microsoft SQL Server database provider.

These folders contain cloud-specific components of BaGet:

* `BaGet.Aliyun` - BaGet's Alibaba Cloud(Aliyun) provider.
* `BaGet.Aws` - BaGet's Amazon Web Services provider.
* `BaGet.Azure` - BaGet's Azure provider.
* `BaGet.Gcp` - BaGet's Google Cloud Platform provider.

