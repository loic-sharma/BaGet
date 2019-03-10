# BaGet's SQLite Database Provider

This project contains BaGet's SQLite database provider.

## Migrations

Add a migration with:

```
dotnet ef migrations add MigrationName --context SqliteContext --output-dir Migrations --startup-project ..\BaGet\BaGet.csproj

dotnet ef database update --context SqliteContext
```
