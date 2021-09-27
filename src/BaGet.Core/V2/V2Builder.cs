using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BaGet.Core
{
    public class V2Builder : IV2Builder
    {
        private readonly IUrlGenerator _url;

        public V2Builder(IUrlGenerator url)
        {
            _url = url;
        }

        public XElement BuildIndex()
        {
            var serviceIndex = _url.GetServiceIndexV2Url();

            return XElement.Parse($@"
<service xmlns=""http://www.w3.org/2007/app"" xmlns:atom=""http://www.w3.org/2005/Atom"" xml:base=""{serviceIndex}"">
  <workspace>
    <atom:title type=""text"">Default</atom:title>
    <collection href=""Packages"">
      <atom:title type=""text"">Packages</atom:title>
    </collection>
  </workspace>
</service>");
        }

        public XElement BuildPackages(IReadOnlyList<Package> packages)
        {
            // See: https://joelverhagen.github.io/NuGetUndocs/#endpoint-find-packages-by-id
            var serviceIndex = _url.GetServiceIndexV2Url();

            // TODO: Add <?xml version="1.0" encoding="utf-8"?> to top
            return new XElement(
                N.feed,
                new XAttribute(N.baze, XNamespace.Get(serviceIndex)),
                new XAttribute(N.m, NS.m),
                new XAttribute(N.d, NS.d),
                new XAttribute(N.georss, NS.georss),
                new XAttribute(N.gml, NS.gml),
                new XElement(N.m_count, packages.Count),

                packages.Select(package =>
                {
                    var packageV2Url = _url.GetPackageVersionV2Url(package);
                    var downloadUrl = _url.GetPackageDownloadUrl(package);

                    return new XElement(
                        N.entry,
                        new XElement(N.id, packageV2Url),
                        new XElement(N.title, package.Title),
                        new XElement(
                            N.content,
                            new XAttribute("type", "application/zip"),
                            new XAttribute("src", downloadUrl)
                        ),

                        BuildAuthor(package),
                        BuildProperties(package)
                    );
                })
            );
        }

        public XElement BuildPackage(Package package)
        {
            // See: https://joelverhagen.github.io/NuGetUndocs/#endpoint-get-a-single-package
            var serviceIndex = _url.GetServiceIndexV2Url();
            var packageV2Url = _url.GetPackageVersionV2Url(package);
            var downloadUrl = _url.GetPackageDownloadUrl(package);

            return new XElement(
                N.entry,
                new XAttribute(N.baze, XNamespace.Get(serviceIndex)),
                new XAttribute(N.m, NS.m),
                new XAttribute(N.d, NS.d),
                new XAttribute(N.georss, NS.georss),
                new XAttribute(N.gml, NS.gml),
                new XElement(N.id, packageV2Url),
                new XElement(N.title, package.Title),

                new XElement(
                    N.content,
                    new XAttribute("type", "application/zip"),
                    new XAttribute("src", downloadUrl)
                ),

                BuildAuthor(package),
                BuildProperties(package)
            );
        }

        private XElement BuildProperties(Package package)
        {
            // See: https://joelverhagen.github.io/NuGetUndocs/#package-entity
            return new XElement(
                N.m_properties,
                new XElement(N.d_Id, package.Id),
                new XElement(N.d_Title, package.Title),
                new XElement(N.d_Version, package.OriginalVersionString),
                new XElement(N.d_NormalizedVersion, package.NormalizedVersionString),
                new XElement(N.d_Authors, string.Join(", ", package.Authors)),
                new XElement(N.d_Copyright, ""), // TODO
                new XElement(N.d_Description, package.Description),
                new XElement(
                    N.d_DownloadCount,
                    new XAttribute(N.m_type, "Edm.Int32"),
                    package.Downloads),
                new XElement(N.d_LastEdited, package.Published),
                new XElement(N.d_Published, package.Published),
                new XElement(N.d_PackageHash, ""),
                new XElement(N.d_PackageHashAlgorithm, ""),
                new XElement(N.d_PackageSize, 0),
                new XElement(N.d_ProjectUrl, package.ProjectUrl),
                new XElement(N.d_IconUrl, package.IconUrl), // TODO, URL logic
                new XElement(N.d_LicenseUrl, package.LicenseUrl), // TODO
                new XElement(N.d_Tags, string.Join(", ", package.Tags)),
                new XElement(N.d_RequireLicenseAcceptance, package.RequireLicenseAcceptance),

                BuildDependencies(package)
            );
        }

        private XElement BuildAuthor(Package package)
        {
            // TODO: No authors?
            return new XElement(
                N.author,
                package.Authors.Select(author => new XElement(N.name, author))
            );
        }

        private XElement BuildDependencies(Package package)
        {
            var flattenedDependencies = new List<string>();

            flattenedDependencies.AddRange(
                package
                    .Dependencies
                    .Where(IsFrameworkDependency)
                    .Select(dependency => dependency.TargetFramework)
                    .Distinct()
                    .Select(targetFramework => $"::{targetFramework}"));

            flattenedDependencies.AddRange(
                package
                    .Dependencies
                    .Where(dependency => !IsFrameworkDependency(dependency))
                    .Select(dependency => $"{dependency.Id}:{dependency.VersionRange}:{dependency.TargetFramework}"));

            var result = string.Join("|", flattenedDependencies);

            return new XElement(N.d_Dependencies, result);
        }

        private bool IsFrameworkDependency(PackageDependency dependency)
        {
            return dependency.Id == null && dependency.VersionRange == null;
        }

        private static class NS
        {
            public static readonly XNamespace xmlns = "http://www.w3.org/2005/Atom";
            //public static readonly XNamespace baze = "https://www.nuget.org/api/v2/curated-feeds/microsoftdotnet";
            public static readonly XNamespace m = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
            public static readonly XNamespace d = "http://schemas.microsoft.com/ado/2007/08/dataservices";
            public static readonly XNamespace georss = "http://www.georss.org/georss";
            public static readonly XNamespace gml = "http://www.opengis.net/gml";
        }

        private static class N
        {
            public static readonly XName feed = NS.xmlns + "feed";
            public static readonly XName entry = NS.xmlns + "entry";
            public static readonly XName title = NS.xmlns + "title";
            public static readonly XName author = NS.xmlns + "author";
            public static readonly XName name = NS.xmlns + "name";
            public static readonly XName link = NS.xmlns + "link";
            public static readonly XName id = NS.xmlns + "id";
            public static readonly XName content = NS.xmlns + "content";

            public static readonly XName m_count = NS.m + "count";
            public static readonly XName m_properties = NS.m + "properties";
            public static readonly XName m_type = NS.m + "type";

            public static readonly XName d_Id = NS.d + "Id";
            public static readonly XName d_Title = NS.d + "Title";
            public static readonly XName d_Version = NS.d + "Version";
            public static readonly XName d_NormalizedVersion = NS.d + "NormalizedVersion";
            public static readonly XName d_Authors = NS.d + "Authors";
            public static readonly XName d_Copyright = NS.d + "Copyright";
            public static readonly XName d_Dependencies = NS.d + "Dependencies";
            public static readonly XName d_Description = NS.d + "Description";
            public static readonly XName d_IconUrl = NS.d + "IconUrl";
            public static readonly XName d_LicenseUrl = NS.d + "LicenseUrl";
            public static readonly XName d_ProjectUrl = NS.d + "ProjectUrl";
            public static readonly XName d_Tags = NS.d + "Tags";
            public static readonly XName d_ReportAbuseUrl = NS.d + "ReportAbuseUrl";
            public static readonly XName d_RequireLicenseAcceptance = NS.d + "RequireLicenseAcceptance";
            public static readonly XName d_DownloadCount = NS.d + "DownloadCount";
            public static readonly XName d_Created = NS.d + "Created";
            public static readonly XName d_LastEdited = NS.d + "LastEdited";
            public static readonly XName d_Published = NS.d + "Published";
            public static readonly XName d_PackageHash = NS.d + "PackageHash";
            public static readonly XName d_PackageHashAlgorithm = NS.d + "PackageHashAlgorithm";
            public static readonly XName d_MinClientVersion = NS.d + "MinClientVersion";
            public static readonly XName d_PackageSize = NS.d + "PackageSize";

            public static readonly XName baze = XNamespace.Xmlns + "base";
            public static readonly XName m = XNamespace.Xmlns + "m";
            public static readonly XName d = XNamespace.Xmlns + "d";
            public static readonly XName georss = XNamespace.Xmlns + "georss";
            public static readonly XName gml = XNamespace.Xmlns + "gml";
        }
    }
}
