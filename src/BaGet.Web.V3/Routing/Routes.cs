namespace BaGet.Web.V3.Routing;

public class Routes
{
    /// <summary>
    /// The route value that carries the repository name on the <c>/r/{repo}/</c> variant of a protocol route.
    /// </summary>
    public const string RepositoryRouteKey = BaGet.Frontend.Mvc.Attributes.RepositoryPageAttribute.RouteKey;

    /// <summary>
    /// The path segments in front of a non-default repository's routes.
    /// </summary>
    public const string RepositoryPathPrefix = "r/{" + RepositoryRouteKey + "}/";

    /// <summary>
    /// The name under which the <c>/r/{repo}/</c> variant of the route <paramref name="routeName"/> is registered;
    /// the unprefixed route keeps <paramref name="routeName"/> and serves the default repository.
    /// </summary>
    public static string RepositoryRouteName(string routeName) => "r:" + routeName;

    public const string IndexRouteName = "index";
    public const string UploadPackageRouteName = "upload-package";
    public const string UploadSymbolRouteName = "upload-symbol";
    public const string DeleteRouteName = "delete";
    public const string RelistRouteName = "relist";
    public const string SearchRouteName = "search";
    public const string AutocompleteRouteName = "autocomplete";
    public const string DependentsRouteName = "dependents";
    public const string RegistrationIndexRouteName = "registration-index";
    public const string RegistrationLeafRouteName = "registration-leaf";
    public const string PackageVersionsRouteName = "package-versions";
    public const string PackageDownloadRouteName = "package-download";
    public const string PackageDownloadManifestRouteName = "package-download-manifest";
    public const string PackageDownloadReadmeRouteName = "package-download-readme";
    public const string PackageDownloadIconRouteName = "package-download-icon";
    public const string SymbolDownloadRouteName = "symbol-download";
    public const string PrefixedSymbolDownloadRouteName = "prefixed-symbol-download";

    public const string V2RootRouteName = "v2-root";
    public const string V2MetadataRouteName = "v2-metadata";
    public const string V2FindPackagesByIdRouteName = "v2-find-packages-by-id";
    public const string V2PackagesRouteName = "v2-packages";
    public const string V2PackagesByKeyRouteName = "v2-packages-by-key";
    public const string V2SearchRouteName = "v2-search";

    public const string SetDeprecationRouteName = "set-deprecation";
    public const string RemoveDeprecationRouteName = "remove-deprecation";
    public const string SetVulnerabilitiesRouteName = "set-vulnerabilities";
    public const string VulnerabilityIndexRouteName = "vulnerability-index";
    public const string VulnerabilityPageRouteName = "vulnerability-page";
    public const string CatalogIndexRouteName = "catalog-index";
    public const string CatalogPageRouteName = "catalog-page";
    public const string CatalogLeafRouteName = "catalog-leaf";
    public const string RepositorySignaturesRouteName = "repository-signatures";
    public const string RepositorySigningCertificateRouteName = "repository-signing-certificate";
    public const string ValidationStatusRouteName = "validation-status";

    /// <summary>
    /// Route names of the npm registry (<see cref="BaGetEndpointBuilder.MapNpmRoutes"/>); a scoped-name variant (the scope as
    /// its own path segment) is registered under the same name plus <see cref="NpmScopedSuffix"/>.
    /// </summary>
    public const string NpmPingRouteName = "npm-ping";
    public const string NpmWhoamiRouteName = "npm-whoami";
    public const string NpmSearchRouteName = "npm-search";
    public const string NpmWebLoginRouteName = "npm-web-login";
    public const string NpmLoginRouteName = "npm-login";
    public const string NpmLogoutRouteName = "npm-logout";
    public const string NpmAuditBulkRouteName = "npm-audit-bulk";
    public const string NpmAuditQuickRouteName = "npm-audit-quick";
    public const string NpmPackumentRouteName = "npm-packument";
    public const string NpmPublishRouteName = "npm-publish";
    public const string NpmPutRevisionRouteName = "npm-put-revision";
    public const string NpmDeletePackageRouteName = "npm-delete-package";
    public const string NpmTarballRouteName = "npm-tarball";
    public const string NpmDeleteTarballRouteName = "npm-delete-tarball";
    public const string NpmDistTagsRouteName = "npm-dist-tags";
    public const string NpmSetDistTagRouteName = "npm-set-dist-tag";
    public const string NpmRemoveDistTagRouteName = "npm-remove-dist-tag";

    /// <summary>Appended to the name of the npm route variant whose scope is a path segment of its own (<c>@{scope}/{pkg}</c>).</summary>
    public const string NpmScopedSuffix = "-scoped";

    /// <summary>Route names of the Maven repository protocol (<see cref="BaGetEndpointBuilder.MapMavenRoutes"/>).</summary>
    public const string MavenGetRouteName = "maven-get";
    public const string MavenPutRouteName = "maven-put";
    public const string MavenDeleteRouteName = "maven-delete";

    /// <summary>Route names of the OCI registry protocol (<see cref="BaGetEndpointBuilder.MapOciRoutes"/>).</summary>
    public const string OciBaseRouteName = "oci-base";
    public const string OciBaseSlashRouteName = "oci-base-slash";
    public const string OciTokenRouteName = "oci-token";
    public const string OciGetRouteName = "oci-get";
    public const string OciPostRouteName = "oci-post";
    public const string OciPatchRouteName = "oci-patch";
    public const string OciPutRouteName = "oci-put";
    public const string OciDeleteRouteName = "oci-delete";
}