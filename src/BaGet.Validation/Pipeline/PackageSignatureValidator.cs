using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.Options;
using NuGet.Common;
using NuGet.Packaging.Signing;

namespace BaGet.Validation.Pipeline;

/// <summary>
/// A signed package must have an intact, valid signature; an unsigned one is rejected
/// only when <see cref="ValidationOptions.RequireSignedPackages"/> is set. Whether the
/// signing certificate is trusted is not checked: that is the client's policy to apply.
/// </summary>
public class PackageSignatureValidator : IPackageValidator
{
    private readonly IOptionsSnapshot<ValidationOptions> _options;

    public PackageSignatureValidator(IOptionsSnapshot<ValidationOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "PackageSignature";

    public async Task<PackageValidatorResult> ValidateAsync(PackageValidationContext context, CancellationToken cancellationToken)
    {
        if (!await context.Reader.IsSignedAsync(cancellationToken))
        {
            return _options.Value.RequireSignedPackages
                ? PackageValidatorResult.Invalid("The package must be signed.")
                : PackageValidatorResult.Valid;
        }

        var verifier = new PackageSignatureVerifier(new ISignatureVerificationProvider[]
        {
            new IntegrityVerificationProvider(),
            new SignatureTrustAndValidityVerificationProvider(),
        });

        // Untrusted roots are allowed: a private feed's packages are often signed with
        // an internal certificate the server's machine does not trust.
        var settings = new SignedPackageVerifierSettings(
            allowUnsigned: false,
            allowIllegal: false,
            allowUntrusted: true,
            allowIgnoreTimestamp: true,
            allowMultipleTimestamps: true,
            allowNoTimestamp: true,
            allowUnknownRevocation: true,
            reportUnknownRevocation: false,
            verificationTarget: VerificationTarget.All,
            signaturePlacement: SignaturePlacement.Any,
            repositoryCountersignatureVerificationBehavior: SignatureVerificationBehavior.IfExistsAndIsNecessary,
            revocationMode: RevocationMode.Offline);

        var result = await verifier.VerifySignaturesAsync(context.Reader, settings, cancellationToken);
        if (result.IsValid)
        {
            return PackageValidatorResult.Valid;
        }

        var errors = result.Results
            .SelectMany(r => r.GetErrorIssues())
            .Select(i => $"{i.Code}: {i.Message}")
            .ToList();

        return PackageValidatorResult.Invalid(
            errors.Count == 0 ? "The package's signature is invalid." : string.Join(" ", errors));
    }
}