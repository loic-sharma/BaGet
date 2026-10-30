using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BaGet.Core.Tests.Support;

/// <summary>
/// Builds a real .nupkg in memory so the package reading code can be exercised
/// without mocking it.
/// </summary>
public static class TestPackage
{
    /// <summary>
    /// The path of the nuspec within the archives this class builds.
    /// </summary>
    public const string NuspecPath = "Package.nuspec";

    /// <summary>
    /// Create a package whose nuspec holds <paramref name="metadata"/> as the inner XML
    /// of its &lt;metadata&gt; element.
    /// </summary>
    public static MemoryStream Create(
        string metadata,
        IDictionary<string, string>? files = null,
        IDictionary<string, byte[]>? binaryFiles = null)
    {
        var nuspec = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"">
  <metadata>
{metadata}
  </metadata>
</package>";

        return Build(nuspec, files, binaryFiles);
    }

    /// <summary>
    /// Create a zip archive shaped like a package, but with no nuspec in it.
    /// </summary>
    public static MemoryStream CreateWithoutNuspec(IDictionary<string, string>? files = null)
    {
        return Build(nuspec: null, files: files);
    }

    /// <summary>
    /// A stream whose content is not a zip archive at all.
    /// </summary>
    public static MemoryStream NotAZipArchive()
    {
        return new MemoryStream(Encoding.UTF8.GetBytes("This is not a package, it is a text file."));
    }

    private static MemoryStream Build(
        string? nuspec,
        IDictionary<string, string>? files,
        IDictionary<string, byte[]>? binaryFiles = null)
    {
        var result = new MemoryStream();

        using (var archive = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (nuspec != null)
            {
                Write(archive, NuspecPath, nuspec);
            }

            foreach (var file in files ?? new Dictionary<string, string>())
            {
                Write(archive, file.Key, file.Value);
            }

            foreach (var file in binaryFiles ?? new Dictionary<string, byte[]>())
            {
                Write(archive, file.Key, file.Value);
            }
        }

        result.Position = 0;
        return result;
    }

    /// <summary>
    /// Create a package with the minimum required metadata, plus whatever extra
    /// elements are supplied.
    /// </summary>
    public static MemoryStream CreateMinimal(
        string extraMetadata = "",
        string id = "Test.Package",
        string version = "1.0.0",
        string authors = "Test Author",
        IDictionary<string, string>? files = null,
        IDictionary<string, byte[]>? binaryFiles = null)
    {
        return Create(
            $@"    <id>{id}</id>
    <version>{version}</version>
    <authors>{authors}</authors>
    <description>A test package.</description>
{extraMetadata}",
            files,
            binaryFiles);
    }

    /// <summary>
    /// Create a real symbol package (.snupkg) in memory: a package whose nuspec declares
    /// the SymbolsPackage package type, as "dotnet pack --include-symbols" produces.
    /// </summary>
    public static MemoryStream CreateSymbols(
        string id = "Test.Package",
        string version = "1.0.0",
        IDictionary<string, string>? files = null,
        IDictionary<string, byte[]>? binaryFiles = null)
    {
        return CreateMinimal(
            extraMetadata: @"    <packageTypes>
      <packageType name=""SymbolsPackage"" />
    </packageTypes>",
            id: id,
            version: version,
            files: files,
            binaryFiles: binaryFiles);
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);

        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        writer.Write(content);
    }

    private static void Write(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path);

        using var stream = entry.Open();

        stream.Write(content, 0, content.Length);
    }
}