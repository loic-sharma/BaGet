# Extension.BaGet.Database.PostgreSql

The PostgreSQL database provider of [BaGet Extension](https://pieroviano.github.io/BaGet/), built on `Npgsql.EntityFrameworkCore.PostgreSQL`.

```powershell
dotnet add package Extension.BaGet.Database.PostgreSql
```

## Usage

Register it in the `BaGetApplication` callback; it activates when `Database:Type` is `PostgreSql`:

```csharp
services.AddBaGetWebApplication(app => app.AddPostgreSqlDatabase());
```

| Setting | Value |
|---|---|
| `Database:Type` | `PostgreSql` |
| `Database:ConnectionString` | e.g. `Host=localhost;Database=baget;Username=baget;Password=...` |

`AddPostgreSqlDatabase` also registers `PostgreSqlDatabaseContextFactory` (`IDatabaseContextFactory`), which opens a `PostgreSqlContext` on any connection string for the Settings page's database copy. PostgreSQL `End` `setval`s the serial/identity sequence past the highest key.

## Migrations (contributors)

The project has its own design-time context factory, so `dotnet ef` needs no startup project and no
running database. The tool version is pinned by `.config/dotnet-tools.json`; run `dotnet tool restore` once.

From this folder:

```
dotnet ef migrations add MigrationName --context PostgreSqlContext --output-dir Migrations
dotnet ef migrations has-pending-model-changes --context PostgreSqlContext
```

A model change needs a migration in **all four** `BaGet.Database.*` projects, and each must then report
no pending model changes. Keep schema changes additive (new tables, nullable foreign keys): SQLite cannot
alter a foreign key in place.

BaGet applies pending migrations itself when it starts.

- List migrations without a server: `dotnet ef migrations list --context PostgreSqlContext`.
- Never run `dotnet ef migrations remove --no-build`: with a stale assembly it deleted the previous migration. Delete the generated files by hand and restore the model snapshot from `HEAD`.
- Entity Framework is 10.0.13; the MySQL provider is `Microting.EntityFrameworkCore.MySql`; all four providers move together.
