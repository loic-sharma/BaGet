# BaGetter's PostgreSql Database Provider

This project contains BaGetter's PostgreSql database provider.

## Migrations

Add a migration with:

```
dotnet ef migrations add MigrationName --context PostgreSqlContext --output-dir Migrations --startup-project ..\BaGet\BaGetter.csproj

dotnet ef database update --context PostgreSqlContext
```
