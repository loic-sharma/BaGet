# Extension.BaGet.Database.Sqlite

The SQLite database provider of [BaGet Extension](https://pieroviano.github.io/BaGet/), built on `Microsoft.EntityFrameworkCore.Sqlite`.

```powershell
dotnet add package Extension.BaGet.Database.Sqlite
```

## Usage

Register it in the `BaGetApplication` callback; it activates when `Database:Type` is `Sqlite`:

```csharp
services.AddBaGetWebApplication(app => app.AddSqliteDatabase());
```

| Setting | Value |
|---|---|
| `Database:Type` | `Sqlite` |
| `Database:ConnectionString` | e.g. `Data Source=baget.db` |

`AddSqliteDatabase` also registers `SqliteDatabaseContextFactory` (`IDatabaseContextFactory`), which opens a `SqliteContext` on any connection string for the Settings page's database copy.

## Migrations (contributors)

The project has its own design-time context factory, so `dotnet ef` needs no startup project and no
running database. The tool version is pinned by `.config/dotnet-tools.json`; run `dotnet tool restore` once.

From this folder:

```
dotnet ef migrations add MigrationName --context SqliteContext --output-dir Migrations
dotnet ef migrations has-pending-model-changes --context SqliteContext
```

A model change needs a migration in **all four** `BaGet.Database.*` projects, and each must then report
no pending model changes. Keep schema changes additive (new tables, nullable foreign keys): SQLite cannot
alter a foreign key in place.

BaGet applies pending migrations itself when it starts.

- List migrations without a server: `dotnet ef migrations list --context SqliteContext`.
- Never run `dotnet ef migrations remove --no-build`: with a stale assembly it deleted the previous migration. Delete the generated files by hand and restore the model snapshot from `HEAD`.
- Entity Framework is 10.0.13; the MySQL provider is `Microting.EntityFrameworkCore.MySql`; all four providers move together.
