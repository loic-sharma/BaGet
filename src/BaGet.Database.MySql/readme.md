# BaGet's MySQL Database Provider

This project contains BaGet's MySQL database provider.

## Migrations

Regenerate migrations with:

```
dotnet ef migrations add MigrationName --context MySqlContext --output-dir Migrations --startup-project ..\BaGet\BaGet.csproj

dotnet ef database update --context MySqlContext
```
