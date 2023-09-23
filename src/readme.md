# BaGetter Source Code

These folders contain the core components of BaGet:

* `BaGet` - The app's entry point that glues everything together.
* `BaGetter.Core` - BaGetter's core logic and services.
* `BaGetter.Web` - The [NuGet server APIs](https://docs.microsoft.com/en-us/nuget/api/overview) and web UI.
* `BaGetter.Protocol` - Libraries to interact with [NuGet servers' APIs](https://docs.microsoft.com/en-us/nuget/api/overview).

These folders contain database-specific components of BaGet:

* `BaGetter.Database.MySql` - BaGetter's MySQL database provider.
* `BaGetter.Database.PostgreSql` - BaGetter's PostgreSql database provider.
* `BaGetter.Database.Sqlite` - BaGetter's SQLite database provider.
* `BaGetter.Database.SqlServer` - BaGetter's Microsoft SQL Server database provider.

These folders contain cloud-specific components of BaGet:

* `BaGetter.Aliyun` - BaGetter's Alibaba Cloud(Aliyun) provider.
* `BaGetter.Aws` - BaGetter's Amazon Web Services provider.
* `BaGetter.Azure` - BaGetter's Azure provider.
* `BaGetter.Gcp` - BaGetter's Google Cloud Platform provider.

