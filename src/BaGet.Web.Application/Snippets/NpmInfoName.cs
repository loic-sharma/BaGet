namespace BaGet.Web.Application.Snippets;

/// <summary>Name helpers that the pages need without the validation of <see cref="BaGet.Core.NpmNames"/>.</summary>
internal static class NpmInfoName
{
    public static string? Scope(string name) => BaGet.Core.NpmNames.Scope(name);
}
