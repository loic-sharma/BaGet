using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using BaGet.Validation.Models;
using BaGet.Validation.Signing;

namespace BaGet.Validation.Controllers.Protocols;

/// <summary>
/// The RepositorySignatures/5.0.0 resource, and each submission's validation status.
/// </summary>
public class RepositorySignaturesController : Controller
{
    // The OID the protocol keys a SHA-256 fingerprint by.
    internal const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    private readonly RepositorySigner _signer;
    private readonly IUrlGenerator _url;

    public RepositorySignaturesController(RepositorySigner signer, IUrlGenerator url)
    {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _url = url ?? throw new ArgumentNullException(nameof(url));
    }

    [HttpGet]
    public IActionResult Index()
    {
        using var certificate = _signer.GetPublicCertificateOrNull();
        if (certificate == null) return NotFound();

        var fingerprint = Fingerprint(certificate.RawData);

        return Json(new RepositorySignaturesResponse
        {
            // Packages pushed before signing was configured, and mirrored ones, are not
            // repository signed, so a client must not insist that every package is.
            AllRepositorySigned = false,
            SigningCertificates = new[]
            {
                new RepositorySigningCertificate
                {
                    Fingerprints = new Dictionary<string, string> { [Sha256Oid] = fingerprint },
                    Subject = certificate.Subject,
                    Issuer = certificate.Issuer,
                    NotBefore = certificate.NotBefore.ToUniversalTime(),
                    NotAfter = certificate.NotAfter.ToUniversalTime(),
                    ContentUrl = _url.GetRepositorySigningCertificateUrl(fingerprint),
                },
            },
        });
    }

    [HttpGet]
    public IActionResult Certificate(string fingerprint)
    {
        using var certificate = _signer.GetPublicCertificateOrNull();
        if (certificate == null || !string.Equals(Fingerprint(certificate.RawData), fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        return File(certificate.RawData, "application/pkix-cert");
    }

    internal static string Fingerprint(byte[] certificate)
        => Convert.ToHexString(SHA256.HashData(certificate)).ToLowerInvariant();
}
