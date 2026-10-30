namespace BaGet.Web.Application.Models.Packages;

public record DependencyModel
{
    public string? PackageId { get; init; }
    public string? VersionSpec { get; init; }
}