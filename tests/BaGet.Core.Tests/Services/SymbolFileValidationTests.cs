using Xunit;

namespace BaGet.Core.Tests.Services;

/// <summary>
/// Entry paths inside a package are matched case-insensitively throughout NuGet, and a
/// symbol package built on a case-sensitive file system can legitimately carry
/// "Library.PDB". These cover the extension checks that decide whether a symbol package
/// is accepted and which of its entries are indexed as PDBs.
/// </summary>
public class SymbolFileValidationTests
{
    public class AreSymbolFilesValid
    {
        [Theory]
        [InlineData("lib/net8.0/Library.pdb")]
        [InlineData("Library.nuspec")]
        [InlineData("lib/net8.0/Library.xml")]
        [InlineData("package/services/metadata/core-properties/abc.psmdcp")]
        [InlineData("_rels/.rels")]
        [InlineData("Library.p7s")]
        public void AcceptsAnAllowedExtension(string entry)
        {
            Assert.True(SymbolIndexingService.AreSymbolFilesValid(new[] { entry }));
        }

        [Theory]
        [InlineData("lib/net8.0/Library.PDB")]
        [InlineData("lib/net8.0/Library.Pdb")]
        [InlineData("Library.NUSPEC")]
        [InlineData("lib/net8.0/Library.XML")]
        public void AcceptsAnAllowedExtensionRegardlessOfCase(string entry)
        {
            Assert.True(SymbolIndexingService.AreSymbolFilesValid(new[] { entry }));
        }

        [Theory]
        [InlineData("lib/net8.0/Library.dll")]
        [InlineData("lib/net8.0/Library.exe")]
        [InlineData("script.ps1")]
        public void RejectsADisallowedExtension(string entry)
        {
            Assert.False(SymbolIndexingService.AreSymbolFilesValid(new[] { entry }));
        }

        [Fact]
        public void RejectsAnEntryWithNoExtension()
        {
            Assert.False(SymbolIndexingService.AreSymbolFilesValid(new[] { "README" }));
        }

        [Fact]
        public void RejectsTheWholePackageWhenAnyEntryIsDisallowed()
        {
            Assert.False(SymbolIndexingService.AreSymbolFilesValid(new[]
            {
                "lib/net8.0/Library.pdb",
                "lib/net8.0/Library.dll",
            }));
        }

        [Fact]
        public void AcceptsAPackageWhoseEntriesAreAllAllowed()
        {
            Assert.True(SymbolIndexingService.AreSymbolFilesValid(new[]
            {
                "Library.nuspec",
                "lib/net8.0/Library.pdb",
                "lib/net8.0/Library.xml",
                "_rels/.rels",
            }));
        }

        [Fact]
        public void AcceptsAnEmptyPackage()
        {
            Assert.True(SymbolIndexingService.AreSymbolFilesValid(new string[0]));
        }
    }

    public class IsPdbPath
    {
        [Theory]
        [InlineData("lib/net8.0/Library.pdb")]
        [InlineData("Library.pdb")]
        public void IsTrueForAPdb(string path)
        {
            Assert.True(SymbolIndexingService.IsPdbPath(path));
        }

        [Theory]
        [InlineData("lib/net8.0/Library.PDB")]
        [InlineData("lib/net8.0/Library.Pdb")]
        public void IsTrueForAPdbRegardlessOfCase(string path)
        {
            // Otherwise the entry passes validation but is never indexed, and the
            // symbol server silently serves nothing for it.
            Assert.True(SymbolIndexingService.IsPdbPath(path));
        }

        [Theory]
        [InlineData("lib/net8.0/Library.dll")]
        [InlineData("Library.nuspec")]
        [InlineData("README")]
        [InlineData("pdb")]
        [InlineData("Library.pdb.txt")]
        public void IsFalseForAnythingElse(string path)
        {
            Assert.False(SymbolIndexingService.IsPdbPath(path));
        }
    }
}