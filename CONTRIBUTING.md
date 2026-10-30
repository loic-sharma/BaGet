## Contribute

Please read the [code of conduct] before contributing.

If you want to run from BaGet Extension's source code:

1. Install the [.NET SDK] (BaGet Extension targets .NET 10)
2. Run `git clone https://github.com/pieroviano/BaGet.git`
3. Create the local package source with `mkdir BaGet/Packages` (nuget.config lists the untracked `Packages` folder as a package source; restore fails with NU1301 without it)
4. Navigate to `./BaGet/src/BaGet`
5. Start the service with `dotnet run -p:GeneratePackageOnBuild=false`
6. Open the URL `http://localhost:6060/v3/index.json` in your browser

Every project is strong-name signed with `SignKey.snk` at the repository root (a checkout without it fails to build).

Always pass `-p:GeneratePackageOnBuild=false` to `dotnet build`, `run` and `test`: `src/Directory.Build.props`
packs on build into the shared `Packages` folder. `dotnet run` may rewrite
`src/BaGet/Properties/launchSettings.json`; revert it rather than committing it.

Run the tests with `dotnet test BaGet.sln -p:GeneratePackageOnBuild=false` from the repository root. They need no
external services: the integration tests use SQLite in a temporary directory. The harness is
`tests/BaGet.Tests/Support/BaGetApplication.cs`; it passes settings with `builder.UseSetting(...)`, not
`ConfigureAppConfiguration`, because accounts are decided while services are registered.

A database change needs a migration for each of the four `src/BaGet.Database.*` providers; see the
readme in any of them.

[Code of conduct]: CODE_OF_CONDUCT.md
[.NET SDK]: https://www.microsoft.com/net/download
