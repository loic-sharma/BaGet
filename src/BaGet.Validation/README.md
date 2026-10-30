# Extension.BaGet.Validation

The validation pipeline of [BaGet Extension](https://pieroviano.github.io/BaGet/): pushed packages are validated,
and optionally repository signed, before they are served.

```powershell
dotnet add package Extension.BaGet.Validation
```

## Usage

`AddBaGetWebApplication` (`Extension.BaGet.Web`) registers it; standalone: `services.AddBaGetValidation()`.
It is always registered; `Validation:Enabled` is read on every push.

Administrators retry, release (index anyway, audited) or reject quarantined submissions of any repository at `/admin/validation` (`ValidationAdministration`).

| Setting | Value |
|---|---|
| `Validation:Enabled` | Off by default. On: a push answers `202 Accepted`, the package appears once every validator passes; a failing package is quarantined |
| `Validation:RequireSignedPackages` | Reject packages without a valid author or repository signature |
| `Validation:RepositorySigningCertificatePath`, `…Password` | PFX that repository-signs every validated package |
| `Validation:RepositorySigningServiceIndexUrl` | This server's public `https` service index URL, recorded in each signature; required for signing |

| Item | Role |
|---|---|
| `IPackageValidator` | Validation step; built-in `PackageEntriesValidator`, `PackageSignatureValidator`. Register more with `TryAddEnumerable` |
| `IValidationQueue` | Push queue; default `InMemoryValidationQueue` |
| `ValidationWorker` | Hosted service that runs the validators |
| Routes | `v3/package/{id}/{version}/validation` (status), `v3/repository-signatures/index.json` |

## Layout

One type per file, namespace = `BaGet.Validation` + folder (`CodeLayoutTests`): `Pipeline` (validators, runner), `Queue`, `Signing`, `Services`, `Models`, `Controllers/Protocols`, `Extensions`.

## Links

[Configuration](https://pieroviano.github.io/BaGet/configuration/) · [Source](https://github.com/pieroviano/BaGet)
