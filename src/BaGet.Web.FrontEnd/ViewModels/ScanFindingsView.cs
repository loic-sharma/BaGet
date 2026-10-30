namespace BaGet.Web.V3.ViewModels;

/// <summary>The version whose scan findings the <c>_ScanFindings</c> partial shows on a package page.</summary>
/// <param name="Name">The artifact name as the format names it (NuGet id, npm name, Maven <c>group:artifact</c>, Docker image).</param>
/// <param name="Version">The version (Docker: the manifest digest).</param>
public sealed record ScanFindingsView(string Name, string Version);
