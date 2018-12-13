using System.Xml.Linq;

namespace BaGet.Legacy
{
    public static class XmlNamespaces
    {
        public static readonly XNamespace xmlns = "http://www.w3.org/2005/Atom";
        //public static readonly XNamespace baze = "https://www.nuget.org/api/v2/curated-feeds/microsoftdotnet";
        public static readonly XNamespace m = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
        public static readonly XNamespace d = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        public static readonly XNamespace georss = "http://www.georss.org/georss";
        public static readonly XNamespace gml = "http://www.opengis.net/gml";
    }
}
