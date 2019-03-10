# BaGet's SQL Server Database Provider

This project contains BaGet's Microsoft SQL Server database provider.

## Migrations

Add a migration with:

```
dotnet ef migrations add MigrationName --context SqlServerContext --output-dir Migrations --startup-project ..\BaGet\BaGet.csproj

dotnet ef database update --context SqlServerContext
```
