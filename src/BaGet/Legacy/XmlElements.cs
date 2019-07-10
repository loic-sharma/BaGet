using System.Xml.Linq;

namespace BaGet.Legacy
{
    public static class XmlElements
    {
        public static readonly XName feed = XmlNamespaces.xmlns + "feed";
        public static readonly XName entry = XmlNamespaces.xmlns + "entry";
        public static readonly XName title = XmlNamespaces.xmlns + "title";
        public static readonly XName author = XmlNamespaces.xmlns + "author";
        public static readonly XName name = XmlNamespaces.xmlns + "name";
        public static readonly XName link = XmlNamespaces.xmlns + "link";
        public static readonly XName id = XmlNamespaces.xmlns + "id";
        public static readonly XName content = XmlNamespaces.xmlns + "content";

        public static readonly XName m_count = XmlNamespaces.m + "count";
        public static readonly XName m_properties = XmlNamespaces.m + "properties";

        public static readonly XName d_Id = XmlNamespaces.d + "Id";
        public static readonly XName d_Title = XmlNamespaces.d + "Title";
        public static readonly XName d_Version = XmlNamespaces.d + "Version";
        public static readonly XName d_NormalizedVersion = XmlNamespaces.d + "NormalizedVersion";
        public static readonly XName d_Authors = XmlNamespaces.d + "Authors";
        public static readonly XName d_Copyright = XmlNamespaces.d + "Copyright";
        public static readonly XName d_Dependencies = XmlNamespaces.d + "Dependencies";
        public static readonly XName d_Description = XmlNamespaces.d + "Description";
        public static readonly XName d_IconUrl = XmlNamespaces.d + "IconUrl";
        public static readonly XName d_LicenseUrl = XmlNamespaces.d + "LicenseUrl";
        public static readonly XName d_ProjectUrl = XmlNamespaces.d + "ProjectUrl";
        public static readonly XName d_Tags = XmlNamespaces.d + "Tags";
        public static readonly XName d_ReportAbuseUrl = XmlNamespaces.d + "ReportAbuseUrl";
        public static readonly XName d_RequireLicenseAcceptance = XmlNamespaces.d + "RequireLicenseAcceptance";
        public static readonly XName d_DownloadCount = XmlNamespaces.d + "DownloadCount";
        public static readonly XName d_Created = XmlNamespaces.d + "Created";
        public static readonly XName d_LastEdited = XmlNamespaces.d + "LastEdited";
        public static readonly XName d_Published = XmlNamespaces.d + "Published";
        public static readonly XName d_PackageHash = XmlNamespaces.d + "PackageHash";
        public static readonly XName d_PackageHashAlgorithm = XmlNamespaces.d + "PackageHashAlgorithm";
        public static readonly XName d_MinClientVersion = XmlNamespaces.d + "MinClientVersion";
        public static readonly XName d_PackageSize = XmlNamespaces.d + "PackageSize";

        public static readonly XName baze = XNamespace.Xmlns + "base";
        public static readonly XName m = XNamespace.Xmlns + "m";
        public static readonly XName d = XNamespace.Xmlns + "d";
        public static readonly XName georss = XNamespace.Xmlns + "georss";
        public static readonly XName gml = XNamespace.Xmlns + "gml";
    }
}
