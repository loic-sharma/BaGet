# BaGetter's MySQL Database Provider

This project contains BaGetter's MySQL database provider.

## Migrations

Add a migration with:

```
dotnet ef migrations add MigrationName --context MySqlContext --output-dir Migrations --startup-project ..\BaGet\BaGetter.csproj

dotnet ef database update --context MySqlContext
```
