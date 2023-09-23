# BaGetter's SQL Server Database Provider

This project contains BaGetter's Microsoft SQL Server database provider.

## Migrations

Add a migration with:

```
dotnet ef migrations add MigrationName --context SqlServerContext --output-dir Migrations --startup-project ..\BaGet\BaGetter.csproj

dotnet ef database update --context SqlServerContext
```
