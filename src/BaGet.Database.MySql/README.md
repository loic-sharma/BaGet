# Extension.BaGet.Database.MySql

The MySQL database provider of [BaGet Extension](https://pieroviano.github.io/BaGet/), built on `Microting.EntityFrameworkCore.MySql` (a maintained Pomelo fork).

```powershell
dotnet add package Extension.BaGet.Database.MySql
```

## Usage

Register it in the `BaGetApplication` callback; it activates when `Database:Type` is `MySql`:

```csharp
services.AddBaGetWebApplication(app => app.AddMySqlDatabase());
```

| Setting | Value |
|---|---|
| `Database:Type` | `MySql` |
| `Database:ConnectionString` | e.g. `Server=localhost;Database=baget;User=root;Password=...` |
| `Database:MySqlServerVersion` | e.g. `8.0.36-mysql`; empty auto-detects (opens a connection at startup) |

`AddMySqlDatabase` also registers `MySqlDatabaseContextFactory` (`IDatabaseContextFactory`), which opens a `MySqlContext` on any connection string for the Settings page's database copy.

## Migrations (contributors)

The project has its own design-time context factory, so `dotnet ef` needs no startup project and no
running database. The tool version is pinned by `.config/dotnet-tools.json`; run `dotnet tool restore` once.

From this folder:

```
dotnet ef migrations add MigrationName --context MySqlContext --output-dir Migrations
dotnet ef migrations has-pending-model-changes --context MySqlContext
```

A model change needs a migration in **all four** `BaGet.Database.*` projects, and each must then report
no pending model changes. Keep schema changes additive (new tables, nullable foreign keys): SQLite cannot
alter a foreign key in place.

BaGet applies pending migrations itself when it starts.

- List migrations without a server: `dotnet ef migrations list --context MySqlContext`.
- Never run `dotnet ef migrations remove --no-build`: with a stale assembly it deleted the previous migration. Delete the generated files by hand and restore the model snapshot from `HEAD`.
- Entity Framework is 10.0.13; the MySQL provider is `Microting.EntityFrameworkCore.MySql`; all four providers move together.

## Schema notes

- The long text columns of `Packages` (authors, description, summary, URLs, tags) are `longtext`: as `varchar(4000)` they exceed MySQL's 65,535-byte row limit and strict-mode servers refuse the table.
