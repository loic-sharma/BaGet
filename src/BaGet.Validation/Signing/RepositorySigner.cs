using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.Options;
using NuGet.Common;
using NuGet.Packaging.Signing;

namespace BaGet.Validation.Signing;

/// <summary>
/// Repository-signs validated packages with the configured certificate, so a client can
/// tell a package came through this server. See:
/// https://learn.microsoft.com/en-us/nuget/api/repository-signatures-resource
/// </summary>
public class RepositorySigner
{
    private readonly IOptionsSnapshot<ValidationOptions> _options;

    public RepositorySigner(IOptionsSnapshot<ValidationOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_options.Value.RepositorySigningCertificatePath);

    /// <summary>
    /// The configured certificate, without its private key, or null if none is.
    /// </summary>
    public X509Certificate2? GetPublicCertificateOrNull()
    {
        if (!IsConfigured) return null;

        using var certificate = LoadCertificate();

        return X509CertificateLoader.LoadCertificate(certificate.RawData);
    }

    /// <summary>
    /// Sign a package, returning the signed copy. A package that already has a
    /// repository signature is returned as it is.
    /// </summary>
    /// <param name="package">The package to sign. It is read, not modified.</param>
    /// <param name="serviceIndexUrl">
    /// This server's service index. The signature records it, and a client checks that
    /// it matches the source the package came from.
    /// </param>
    /// <param name="cancellationToken">Cancels signing.</param>
    public async Task<Stream> SignAsync(Stream package, string serviceIndexUrl, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "BaGetSigning", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var input = Path.Combine(directory, "input.nupkg");
        var output = Path.Combine(directory, "output.nupkg");

        try
        {
            using (var file = File.Create(input))
            {
                package.Position = 0;
                await package.CopyToAsync(file, cancellationToken);
            }

            using (var certificate = LoadCertificate())
            using (var request = new RepositorySignPackageRequest(
                       certificate,
                       NuGet.Common.HashAlgorithmName.SHA256,
                       NuGet.Common.HashAlgorithmName.SHA256,
                       new Uri(serviceIndexUrl),
                       packageOwners: null))
            using (var options = SigningOptions.CreateFromFilePaths(
                       input,
                       output,
                       overwrite: true,
                       signatureProvider: new X509SignatureProvider(timestampProvider: null),
                       logger: NullLogger.Instance))
            {
                await SigningUtility.SignAsync(options, request, cancellationToken);
            }

            var signed = new MemoryStream();
            using (var file = File.OpenRead(output))
            {
                await file.CopyToAsync(signed, cancellationToken);
            }

            signed.Position = 0;

            return signed;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A temporary file left behind is not worth failing the push for.
            }
        }
    }

    #pragma warning disable CS8604
    private X509Certificate2 LoadCertificate()
        => X509CertificateLoader.LoadPkcs12FromFile(
            _options.Value.RepositorySigningCertificatePath,
            _options.Value.RepositorySigningCertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
    #pragma warning restore CS8604
}