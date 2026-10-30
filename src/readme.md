# BaGet Source Code

Each project is packed as `Extension.<project>`; its `README.md` is the package readme.

These folders contain the core components of BaGet:

* `BaGet` - The app's entry point and command line (`import downloads`, `catalog rebuild`, `account reset-password|promote|demote <user>`).
* `BaGet.Core` - BaGet's core logic and services: repositories (local, remote, virtual), format modules (NuGet, npm, Maven, Docker), access model, retention, scanning, replication, high availability.
* `BaGet.Web` - The [NuGet server APIs](https://docs.microsoft.com/en-us/nuget/api/overview), the npm, Maven and OCI endpoints, the `/api/v1` administration API and the web UI.
* `BaGet.Web.V2` - The NuGet V2 (OData) feed, under `/api/v2`.
* `BaGet.Catalog` - The NuGet V3 [catalog](https://learn.microsoft.com/nuget/api/catalog-resource), for mirrors.
* `BaGet.Identity` - Accounts, package owners, organizations and per-user API keys, and their pages, plus the admin pages (access model, retention, replication, scanning).
* `BaGet.Validation` - The validation pipeline and repository signing.
* `BaGet.Protocol` - Libraries to interact with [NuGet servers' APIs](https://docs.microsoft.com/en-us/nuget/api/overview).

These folders contain database-specific components of BaGet:

* `BaGet.Database.MySql` - BaGet's MySQL database provider.
* `BaGet.Database.PostgreSql` - BaGet's PostgreSql database provider.
* `BaGet.Database.Sqlite` - BaGet's SQLite database provider.
* `BaGet.Database.SqlServer` - BaGet's Microsoft SQL Server database provider.

These folders contain cloud-specific components of BaGet:

* `BaGet.Aliyun` - BaGet's Alibaba Cloud(Aliyun) provider.
* `BaGet.Aws` - BaGet's Amazon Web Services provider.
* `BaGet.Azure` - BaGet's Azure provider (Blob Storage, Table Storage, Azure Search).
* `BaGet.Gcp` - BaGet's Google Cloud Platform provider.
