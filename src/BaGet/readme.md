# BaGet

This is the project that implements [NuGet service APIs](https://docs.microsoft.com/en-us/nuget/api/overview). Most of the core logic is contained within the `BaGet.Core` project.

## Migrations

Regenerate migrations with:

```
rm baget.db
dotnet ef migrations add MigrationName --context SqliteContext --output-dir Migrations/Sqlite
dotnet ef migrations add MigrationName --context SqlServerContext --output-dir Migrations/SqlServer
dotnet ef migrations add MigrationName --context MysqlContext --output-dir Migrations/Mysql

dotnet ef database update --context SqliteContext
```
