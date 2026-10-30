using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using BaGet.Web.V2.Controllers;
using BaGet.Web.V2.Models;

namespace BaGet.Web.V2.Services;

/// <summary>
/// Writes the Atom documents the NuGet V2 protocol is built on.
/// </summary>
/// <remarks>
/// Written by hand rather than with an OData library: only a handful of endpoints are
/// needed and the shape is fixed. The cost is that the OData query options are not
/// interpreted - see <see cref="V2FeedController"/>.
/// </remarks>
internal static class V2FeedWriter
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace DataServices = "http://schemas.microsoft.com/ado/2007/08/dataservices";
    private static readonly XNamespace Metadata = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
    private static readonly XNamespace Edm = "http://schemas.microsoft.com/ado/2009/11/edm";
    private static readonly XNamespace Edmx = "http://schemas.microsoft.com/ado/2007/06/edmx";

    /// <summary>
    /// The properties the feed publishes, with their EDM types. One list drives both the
    /// metadata document and the entries, so the two cannot drift apart.
    /// </summary>
    private static readonly (string Name, string Type, bool Nullable)[] Properties =
    {
        ("Version", "Edm.String", false),
        ("NormalizedVersion", "Edm.String", true),
        ("Authors", "Edm.String", true),
        ("Description", "Edm.String", true),
        ("Summary", "Edm.String", true),
        ("Title", "Edm.String", true),
        ("Tags", "Edm.String", true),
        ("Language", "Edm.String", true),
        ("ReleaseNotes", "Edm.String", true),
        ("MinClientVersion", "Edm.String", true),
        ("Dependencies", "Edm.String", true),
        ("IconUrl", "Edm.String", true),
        ("LicenseUrl", "Edm.String", true),
        ("ProjectUrl", "Edm.String", true),
        ("DownloadCount", "Edm.Int64", false),
        ("RequireLicenseAcceptance", "Edm.Boolean", false),
        ("Listed", "Edm.Boolean", false),
        ("IsPrerelease", "Edm.Boolean", false),
        ("IsLatestVersion", "Edm.Boolean", false),
        ("IsAbsoluteLatestVersion", "Edm.Boolean", false),
        ("Published", "Edm.DateTime", false),
        ("LastUpdated", "Edm.DateTime", false),
    };

    public static string WriteMetadata()
    {
        // Every entry is a media link entry: its <content src> is the .nupkg (HasStream).
        var entityType = new XElement(
            Edm + "EntityType",
            new XAttribute("Name", "V2FeedPackage"),
            new XAttribute(Metadata + "HasStream", "true"),
            new XElement(
                Edm + "Key",
                new XElement(Edm + "PropertyRef", new XAttribute("Name", "Id")),
                new XElement(Edm + "PropertyRef", new XAttribute("Name", "Version"))),
            new XElement(
                Edm + "Property",
                new XAttribute("Name", "Id"),
                new XAttribute("Type", "Edm.String"),
                new XAttribute("Nullable", "false")));

        foreach (var (name, type, nullable) in Properties)
        {
            // Version is part of the key, so it is never nullable.
            var isNullable = name != "Version" && nullable;

            entityType.Add(new XElement(
                Edm + "Property",
                new XAttribute("Name", name),
                new XAttribute("Type", type),
                new XAttribute("Nullable", isNullable ? "true" : "false")));
        }

        var document = new XDocument(
            new XElement(
                Edmx + "Edmx",
                new XAttribute("Version", "1.0"),
                new XAttribute(XNamespace.Xmlns + "edmx", Edmx.NamespaceName),
                new XElement(
                    Edmx + "DataServices",
                    new XAttribute(Metadata + "DataServiceVersion", "2.0"),
                    new XAttribute(Metadata + "MaxDataServiceVersion", "3.0"),
                    new XAttribute(XNamespace.Xmlns + "m", Metadata.NamespaceName),
                    new XElement(
                        Edm + "Schema",
                        new XAttribute("Namespace", "NuGetGallery"),
                        entityType,
                        new XElement(
                            Edm + "EntityContainer",
                            new XAttribute("Name", "V2FeedContext"),
                            new XAttribute(Metadata + "IsDefaultEntityContainer", "true"),
                            new XElement(
                                Edm + "EntitySet",
                                new XAttribute("Name", "Packages"),
                                new XAttribute("EntityType", "NuGetGallery.V2FeedPackage")),
                            // The service operations the feed answers (V2FeedController.Search and FindPackagesById), with the
                            // parameters they bind; $skip/$top are query options, not parameters.
                            FunctionImport(
                                "Search",
                                ("searchTerm", "Edm.String", true),
                                ("includePrerelease", "Edm.Boolean", false),
                                ("semVerLevel", "Edm.String", true)),
                            FunctionImport(
                                "FindPackagesById",
                                ("id", "Edm.String", true),
                                ("semVerLevel", "Edm.String", true)))))));

        return Serialize(document);
    }

    /// <summary>A GET service operation returning packages of the <c>Packages</c> set.</summary>
    private static XElement FunctionImport(string name, params (string Name, string Type, bool Nullable)[] parameters)
    {
        var function = new XElement(
            Edm + "FunctionImport",
            new XAttribute("Name", name),
            new XAttribute("ReturnType", "Collection(NuGetGallery.V2FeedPackage)"),
            new XAttribute("EntitySet", "Packages"),
            new XAttribute(Metadata + "HttpMethod", "GET"));

        foreach (var (parameterName, type, nullable) in parameters)
        {
            function.Add(new XElement(
                Edm + "Parameter",
                new XAttribute("Name", parameterName),
                new XAttribute("Type", type),
                new XAttribute("Nullable", nullable ? "true" : "false")));
        }

        return function;
    }

    public static string WriteFeed(
        IReadOnlyList<V2FeedPackage> packages,
        string feedId,
        string feedTitle,
        string selfUrl,
        string baseUrl,
        DateTime updated,
        string? nextUrl = null)
    {
        var feed = new XElement(
            Atom + "feed",
            new XAttribute(XNamespace.Xml + "base", baseUrl),
            new XAttribute(XNamespace.Xmlns + "d", DataServices.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "m", Metadata.NamespaceName),
            new XElement(Atom + "id", feedId),
            new XElement(Atom + "title", new XAttribute("type", "text"), feedTitle),
            new XElement(Atom + "updated", Timestamp(updated)),
            new XElement(
                Atom + "link",
                new XAttribute("rel", "self"),
                new XAttribute("href", selfUrl)));

        foreach (var package in packages)
        {
            feed.Add(WriteEntryElement(package, baseUrl));
        }

        // A "next" link is how V2 pages: a client follows it until it is absent.
        if (!string.IsNullOrEmpty(nextUrl))
        {
            feed.Add(new XElement(
                Atom + "link",
                new XAttribute("rel", "next"),
                new XAttribute("href", nextUrl)));
        }

        return Serialize(new XDocument(feed));
    }

    public static string WriteEntry(V2FeedPackage package, string baseUrl)
    {
        var entry = WriteEntryElement(package, baseUrl);

        entry.Add(new XAttribute(XNamespace.Xml + "base", baseUrl));
        entry.Add(new XAttribute(XNamespace.Xmlns + "d", DataServices.NamespaceName));
        entry.Add(new XAttribute(XNamespace.Xmlns + "m", Metadata.NamespaceName));

        return Serialize(new XDocument(entry));
    }

    private static XElement WriteEntryElement(V2FeedPackage package, string baseUrl)
    {
        var properties = new XElement(Metadata + "properties");

        foreach (var (name, type, _) in Properties)
        {
            properties.Add(Property(name, type, Value(package, name)));
        }

        #pragma warning disable CS8604
        return new XElement(
            Atom + "entry",
            new XElement(Atom + "id", EntityId(baseUrl, package)),
            new XElement(Atom + "title", new XAttribute("type", "text"), package.Id),
            new XElement(Atom + "summary", new XAttribute("type", "text"), package.Summary ?? string.Empty),
            new XElement(Atom + "updated", Timestamp(package.LastUpdated)),
            new XElement(Atom + "author", new XElement(Atom + "name", package.Authors ?? string.Empty)),
            new XElement(
                Atom + "content",
                new XAttribute("type", "application/zip"),
                new XAttribute("src", package.DownloadUrl)),
            new XElement(
                Atom + "category",
                new XAttribute("term", "NuGetGallery.V2FeedPackage"),
                new XAttribute("scheme", "http://schemas.microsoft.com/ado/2007/08/dataservices/scheme")),
            properties);
        #pragma warning restore CS8604
    }

    /// <summary>
    /// The V2 key is (Id, Version), written as OData key literals.
    /// </summary>
    internal static string EntityId(string baseUrl, V2FeedPackage package)
    {
        return baseUrl.TrimEnd('/')
               + "/Packages(Id='" + package.Id + "',Version='" + package.Version + "')";
    }

    private static XElement Property(string name, string type, string? value)
    {
        var element = new XElement(DataServices + name);

        if (type != "Edm.String")
        {
            element.Add(new XAttribute(Metadata + "type", type));
        }

        element.Add(value);

        return element;
    }

    private static string? Value(V2FeedPackage p, string name) => name switch
    {
        "Version" => p.Version,
        "NormalizedVersion" => p.NormalizedVersion,
        "Authors" => p.Authors,
        "Description" => p.Description,
        "Summary" => p.Summary,
        "Title" => p.Title,
        "Tags" => p.Tags,
        "Language" => p.Language,
        "ReleaseNotes" => p.ReleaseNotes,
        "MinClientVersion" => p.MinClientVersion,
        "Dependencies" => p.Dependencies,
        "IconUrl" => p.IconUrl,
        "LicenseUrl" => p.LicenseUrl,
        "ProjectUrl" => p.ProjectUrl,
        "DownloadCount" => p.DownloadCount.ToString(CultureInfo.InvariantCulture),
        "RequireLicenseAcceptance" => Boolean(p.RequireLicenseAcceptance),
        "Listed" => Boolean(p.Listed),
        "IsPrerelease" => Boolean(p.IsPrerelease),
        "IsLatestVersion" => Boolean(p.IsLatestVersion),
        "IsAbsoluteLatestVersion" => Boolean(p.IsAbsoluteLatestVersion),
        "Published" => Timestamp(p.Published),
        "LastUpdated" => Timestamp(p.LastUpdated),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown feed property."),
    };

    private static string Boolean(bool value) => value ? "true" : "false";

    private static string Timestamp(DateTime value)
    {
        // Edm.DateTime carries no offset, so the value has to already be UTC.
        return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }

    private static string Serialize(XDocument document)
    {
        // The declaration is written by hand. Saving to a TextWriter makes XDocument
        // declare that writer's encoding - utf-16 for a StringWriter - which then
        // contradicts the utf-8 the response is actually sent as, and a real
        // XmlReader rejects the document with "There is no Unicode byte order mark".
        var builder = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
        };

        using (var writer = XmlWriter.Create(builder, settings))
        {
            document.Save(writer);
        }

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + builder.ToString();
    }
}
