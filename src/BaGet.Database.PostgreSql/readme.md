# BaGet's PostgreSql Database Provider

This project contains BaGet's PostgreSql database provider.

## Migrations

Add a migration with:

```
dotnet ef migrations add MigrationName --context PostgreSqlContext --output-dir Migrations --startup-project ..\BaGet\BaGet.csproj

dotnet ef database update --context PostgreSqlContext
```
