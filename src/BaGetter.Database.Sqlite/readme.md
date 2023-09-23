# BaGetter's SQLite Database Provider

This project contains BaGetter's SQLite database provider.

## Migrations

Add a migration with:

```
dotnet ef migrations add MigrationName --context SqliteContext --output-dir Migrations --startup-project ..\BaGet\BaGetter.csproj

dotnet ef database update --context SqliteContext
```
