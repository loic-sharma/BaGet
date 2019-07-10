using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace BaGet.Legacy
{
    public class ODataPackageSerializer : IODataPackageSerializer
    {
        public void Serialize(Stream outputStream, ODataPackage package, string serviceBaseUrl, string resourceIdUrl, string packageContentUrl)
        {
            var doc = new XElement(XmlElements.entry,
                new XAttribute(XmlElements.baze, XNamespace.Get(serviceBaseUrl)),
                new XAttribute(XmlElements.m, XmlNamespaces.m),
                new XAttribute(XmlElements.d, XmlNamespaces.d),
                new XAttribute(XmlElements.georss, XmlNamespaces.georss),
                new XAttribute(XmlElements.gml, XmlNamespaces.gml),
                new XElement(XmlElements.id, resourceIdUrl),
                new XElement(XmlElements.title, package.Title),
                new XElement(XmlElements.author, new XElement(XmlElements.name, package.Authors)),
                new XElement(
                    XmlElements.content,
                    new XAttribute("type", "application/zip"),
                    new XAttribute("src", packageContentUrl)
                ),
                GetProperties(package)
            );
            var writer = XmlWriter.Create(outputStream);
            doc.WriteTo(writer);
            writer.Flush();
        }

        private static XElement GetProperties(ODataPackage package)
        {
            return new XElement(
                                XmlElements.m_properties,
                                new XElement(XmlElements.d_Id, package.Id),
                                new XElement(XmlElements.d_Title, package.Title),
                                new XElement(XmlElements.d_Version, package.Version),
                                new XElement(XmlElements.d_NormalizedVersion, package.NormalizedVersion),
                                new XElement(XmlElements.d_Authors, package.Authors),
                                new XElement(XmlElements.d_Copyright, package.Copyright),
                                new XElement(XmlElements.d_Dependencies, package.Dependencies),
                                new XElement(XmlElements.d_Description, package.Description),
                                new XElement(XmlElements.d_DownloadCount, package.DownloadCount), //TODO m:type="Edm.Int32"
                                new XElement(XmlElements.d_LastEdited, package.LastUpdated),
                                new XElement(XmlElements.d_Published, package.Published),
                                new XElement(XmlElements.d_PackageHash, package.PackageHash),
                                new XElement(XmlElements.d_PackageHashAlgorithm, package.PackageHashAlgorithm),
                                new XElement(XmlElements.d_PackageSize, package.PackageSize),
                                new XElement(XmlElements.d_ProjectUrl, package.ProjectUrl),
                                new XElement(XmlElements.d_IconUrl, package.IconUrl),
                                new XElement(XmlElements.d_LicenseUrl, package.LicenseUrl),
                                //new XElement(XmlElements.d_ReportAbuseUrl, package.ReportAbuseUrl),
                                new XElement(XmlElements.d_Tags, package.Tags),
                                new XElement(XmlElements.d_RequireLicenseAcceptance, package.RequireLicenseAcceptance)
                            );
        }

        public void Serialize(Stream outputStream, IEnumerable<PackageWithUrls> packages, string serviceBaseUrl)
        {
            var list = packages.ToList();
            var doc = new XElement(
                XmlElements.feed,
                new XAttribute(XmlElements.baze, XNamespace.Get(serviceBaseUrl)),
                new XAttribute(XmlElements.m, XmlNamespaces.m),
                new XAttribute(XmlElements.d, XmlNamespaces.d),
                new XAttribute(XmlElements.georss, XmlNamespaces.georss),
                new XAttribute(XmlElements.gml, XmlNamespaces.gml),
                new XElement(XmlElements.m_count, list.Count),
                list.Select(x =>
                    new XElement(
                        XmlElements.entry,
                        new XElement(XmlElements.id, x.ResourceIdUrl),
                        new XElement(XmlElements.title, x.Pkg.Title),
                        new XElement(XmlElements.author, new XElement(XmlElements.name, x.Pkg.Authors)),
                        new XElement(
                            XmlElements.content,
                            new XAttribute("type", "application/zip"),
                            new XAttribute("src", x.PackageContentUrl)
                        ),
                        GetProperties(x.Pkg)
                    )
                )
            );
            var writer = XmlWriter.Create(outputStream);
            doc.WriteTo(writer);
            writer.Flush();
        }
    }
}
