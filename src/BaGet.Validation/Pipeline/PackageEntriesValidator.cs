using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Validation.Pipeline;

/// <summary>
/// Every entry's path must be safe to extract: none may escape the package's folder.
/// </summary>
public class PackageEntriesValidator : IPackageValidator
{
    public string Name => "PackageEntries";

    public async Task<PackageValidatorResult> ValidateAsync(PackageValidationContext context, CancellationToken cancellationToken)
    {
        try
        {
            await context.Reader.ValidatePackageEntriesAsync(cancellationToken);

            return PackageValidatorResult.Valid;
        }
        catch (NuGet.Packaging.Core.PackagingException e)
        {
            return PackageValidatorResult.Invalid(e.Message);
        }
    }
}
