using System;

namespace BaGet.Validation.Pipeline;

public sealed class PackageValidatorResult
{
    public static readonly PackageValidatorResult Valid = new(true, null);

    private PackageValidatorResult(bool isValid, string? message)
    {
        IsValid = isValid;
        Message = message;
    }

    public bool IsValid { get; }

    /// <summary>
    /// Why the package is invalid. Shown to the package's owners.
    /// </summary>
    public string? Message { get; }

    public static PackageValidatorResult Invalid(string message) =>
        new(false, message ?? throw new ArgumentNullException(nameof(message)));
}