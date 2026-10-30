# Extension.BaGet.Core

The core libraries of [BaGet Extension](https://pieroviano.github.io/BaGet/): domain entities, options, indexing,
storage, search and mirroring. Every other BaGet package depends on it.

```powershell
dotnet add package Extension.BaGet.Core
```

## Extension points

| Type / method | Use |
|---|---|
| `BaGetApplication` | Passed to `AddBaGetWebApplication`; provider packages add `AddXyz()` extensions to it |
| `AddFileStorage()`, `AddNullStorage()`, `AddNullSearch()` | Built-in storage and search providers |
| `IStorageService`, `IPackageDatabase`, `ISearchService`, `ISearchIndexer` | Subsystems resolved at runtime through `IProvider<T>` chains, chosen by `Storage:Type`, `Database:Type`, `Search:Type` |
| `IPackageAuthorizationService` | Push/delete authorization; accounts plug in here |
| `IDatabaseContextFactory` | Opens an `IContext` on any connection string; each relational provider registers one (none for Azure Table). Used by the Settings page's database copy |
| `MaintenanceGate` | In-memory read-only switch: while closed, `MaintenanceSaveChangesInterceptor` makes `SaveChanges` throw `MaintenanceException`. `AllowWrites(builder)` exempts a context |
| `IPackageChangeHandler` | Told about every package change (catalog, validation) |
| `IServiceIndexResourceProvider` | Adds resources to the service index |
| `AddBaGetOptions<T>(section)` | Binds an options type and records it in the settings schema |

## Folders

| Folder | Content |
|---|---|
| Authentication | API key authentication, `IPackageAuthorizationService` |
| Configuration | Options (`BaGetOptions`, `AccountsOptions`, `ValidationOptions`, ...) |
| Content | Serves package content |
| Entities | Database model shared by every database provider; `IContext` (incl. `BeginKeyedInsertAsync`/`EndKeyedInsertAsync` for explicit-key inserts), the read-only gate, and `Entities/Copy` (`DatabaseCopyService`: dependency-ordered, batched, verified copy to another database) |
| Indexing | Package and symbol upload, `IPackageChangeHandler` |
| Metadata | Registration (package metadata) responses |
| Repositories | `Repository`, `Project`, `IRepositoryContext` (the repository in effect, scoped), `IRepositoryService`, `RepositoryNames` |
| Search | Package search |
| ServiceIndex | Service index, `IServiceIndexResourceProvider` |
| Staging | `IPackageStagingService`: holds a browser upload (`/upload`) between preview and confirmation |
| Statistics | Feed-wide download statistics |
| Storage | Package and symbol content storage |
| Upstream | Upstream NuGet feed interactions, for mirroring |
| Validation | Validation of BaGet's own configuration (package validation is in `Extension.BaGet.Validation`) |

## Links

[Documentation](https://pieroviano.github.io/BaGet/) · [Architecture](https://pieroviano.github.io/BaGet/advanced/architecture/) · [Source](https://github.com/pieroviano/BaGet)
