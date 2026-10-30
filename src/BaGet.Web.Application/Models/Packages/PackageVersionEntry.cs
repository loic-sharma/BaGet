using System;

namespace BaGet.Web.Application.Models.Packages;

/// <summary>One row of the versions table of the package page.</summary>
/// <param name="Version">The normalized version.</param>
/// <param name="Downloads">Its downloads.</param>
/// <param name="Selected">Whether it is the version shown.</param>
/// <param name="LastUpdated">When it was published.</param>
public sealed record PackageVersionEntry(string Version, long Downloads, bool Selected, DateTime LastUpdated);
