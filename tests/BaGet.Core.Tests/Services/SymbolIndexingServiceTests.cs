﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Tests.Support;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests.Services;

/// <summary>
/// These drive <see cref="SymbolIndexingService.IndexAsync"/> with real .snupkg and .nupkg
/// archives built in memory, carrying assemblies and portable PDBs a compiler really
/// produced, so that every rejection reason is reached through the production code path.
/// </summary>
public class SymbolIndexingServiceTests
{
    private const string PackageId = "Test.Package";
    private const string PackageVersion = "1.0.0";

    // Two unrelated assemblies with their real portable PDBs. Entry names inside the test
    // packages are deliberately not the assemblies' own names: nothing in the service may
    // depend on a file name, only on the paths lining up and the signatures matching.
    private static readonly byte[] LibraryDll = TestAssembly.ReadAssembly(typeof(SymbolIndexingService).Assembly);
    private static readonly byte[] LibraryPdb = TestAssembly.ReadPdb(typeof(SymbolIndexingService).Assembly);
    private static readonly Guid LibrarySignature = TestAssembly.DebugSignature(typeof(SymbolIndexingService).Assembly);

    private static readonly byte[] OtherDll = TestAssembly.ReadAssembly(typeof(SymbolIndexingServiceTests).Assembly);
    private static readonly byte[] OtherPdb = TestAssembly.ReadPdb(typeof(SymbolIndexingServiceTests).Assembly);
    private static readonly Guid OtherSignature = TestAssembly.DebugSignature(typeof(SymbolIndexingServiceTests).Assembly);

    private readonly Mock<IPackageDatabase> _packages;
    private readonly Mock<IPackageStorageService> _packageStorage;
    private readonly Mock<ISymbolStorageService> _symbols;
    private readonly RecordingLogger<SymbolIndexingService> _logger;
    private readonly List<SavedPdb> _saved;
    private readonly SymbolIndexingService _target;

    public SymbolIndexingServiceTests()
    {
        _packages = new Mock<IPackageDatabase>();
        _packageStorage = new Mock<IPackageStorageService>();
        _symbols = new Mock<ISymbolStorageService>();
        _logger = new RecordingLogger<SymbolIndexingService>();
        _saved = new List<SavedPdb>();

        _symbols
            .Setup(s => s.SavePortablePdbContentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .Callback((string file, string key, Stream content, CancellationToken _) =>
            {
                using var copy = new MemoryStream();
                content.CopyTo(copy);

                _saved.Add(new SavedPdb(file, key, copy.ToArray()));
            })
            .Returns(Task.CompletedTask);

        _target = new SymbolIndexingService(
            _packages.Object,
            _packageStorage.Object,
            _symbols.Object,
            _logger);
    }

    [Fact]
    public async Task IfSnupkgHasEntryThatExtractsOutsideOfExtractionPath_ReturnsInvalidSymbolPackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // An entry whose path climbs out of the extraction directory would overwrite files
        // elsewhere on the server when extracted.
        using var snupkg = TestPackage.CreateSymbols(
            files: new Dictionary<string, string> { { "../../evil.pdb", "not a pdb" } });

        var result = await _target.IndexAsync(snupkg, cancellationToken);

        Assert.Equal(SymbolIndexingResult.InvalidSymbolPackage, result);
        Assert.Empty(_saved);
        AssertRejectionWasExplained("not safe to extract");

        // The package is rejected before it is even looked up.
        _packages.Verify(
            p => p.FindOrNullAsync(
                It.IsAny<string>(),
                It.IsAny<NuGetVersion>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IfSnupkgContainsEntryWithInvalidExtension_ReturnsInvalidSymbolPackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // A symbol package may only carry symbols and packaging metadata. An assembly in
        // there would let an upload replace the package's own content.
        GivenPackageExists(TestPackage.CreateMinimal(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.dll", LibraryDll } }));

        using var snupkg = TestPackage.CreateSymbols(
            binaryFiles: new Dictionary<string, byte[]>
            {
                { "lib/net8.0/Library.pdb", LibraryPdb },
                { "lib/net8.0/Library.dll", LibraryDll },
            });

        var result = await _target.IndexAsync(snupkg, cancellationToken);

        Assert.Equal(SymbolIndexingResult.InvalidSymbolPackage, result);
        Assert.Empty(_saved);
        AssertRejectionWasExplained("lib/net8.0/Library.dll");
    }

    [Fact]
    public async Task IfSnupkgDoesNotHaveCorrespondingNupkg_ReturnsPackageNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        GivenPackageDoesNotExist();

        using var snupkg = TestPackage.CreateSymbols(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.pdb", LibraryPdb } });

        var result = await _target.IndexAsync(snupkg, cancellationToken);

        Assert.Equal(SymbolIndexingResult.PackageNotFound, result);
        Assert.Empty(_saved);
    }

    [Fact]
    public async Task IfSnupkgContainsInvalidPortablePdb_ReturnsInvalidSymbolPackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Have two pdb entries, the first of which is valid. Ensure nothing is persisted.
        GivenPackageExists(TestPackage.CreateMinimal(
            binaryFiles: new Dictionary<string, byte[]>
            {
                { "lib/net8.0/Library.dll", LibraryDll },
                { "lib/net8.0/Broken.dll", OtherDll },
            }));

        using var snupkg = TestPackage.CreateSymbols(
            files: new Dictionary<string, string> { { "lib/net8.0/Broken.pdb", "this is not a pdb at all" } },
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.pdb", LibraryPdb } });

        var result = await _target.IndexAsync(snupkg, cancellationToken);

        Assert.Equal(SymbolIndexingResult.InvalidSymbolPackage, result);
        Assert.Empty(_saved);
        AssertRejectionWasExplained("lib/net8.0/Broken.pdb", "not a portable PDB");
        AssertNothingWasLoggedAsAnException();
    }

    [Fact]
    public async Task IfSnupkgContainsWindowsPdb_ReturnsInvalidSymbolPackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // A Windows PDB is a different format entirely - the symbol server cannot key it -
        // so it must be rejected rather than crash the metadata reader.
        GivenPackageExists(TestPackage.CreateMinimal(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.dll", LibraryDll } }));

        using var snupkg = TestPackage.CreateSymbols(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.pdb", WindowsPdbHeader() } });

        var result = await _target.IndexAsync(snupkg, cancellationToken);

        Assert.Equal(SymbolIndexingResult.InvalidSymbolPackage, result);
        Assert.Empty(_saved);
        AssertRejectionWasExplained("lib/net8.0/Library.pdb", "not a portable PDB");
        AssertNothingWasLoggedAsAnException();
    }

    [Fact]
    public async Task IfSnupkgContainsEmptyPdb_ReturnsInvalidSymbolPackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Shorter than the portable PDB signature: the signature check must not read past
        // the end of the entry.
        GivenPackageExists(TestPackage.CreateMinimal(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.dll", LibraryDll } }));

        using var snupkg = TestPackage.CreateSymbols(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.pdb", new byte[0] } });

        var result = await _target.IndexAsync(snupkg, cancellationToken);

        Assert.Equal(SymbolIndexingResult.InvalidSymbolPackage, result);
        Assert.Empty(_saved);
        AssertRejectionWasExplained("lib/net8.0/Library.pdb", "not a portable PDB");
        AssertNothingWasLoggedAsAnException();
    }

    [Fact]
    public async Task IfSnupkgPdbDoesNotHaveCorrespondingNupkgDll_ReturnsInvalidSymbolPackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // The package ships no assembly at the PDB's path, so the symbols describe code
        // that is not in the package.
        GivenPackageExists(TestPackage.CreateMinimal(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Elsewhere.dll", LibraryDll } }));

        using var snupkg = TestPackage.CreateSymbols(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.pdb", LibraryPdb } });

        var result = await _target.IndexAsync(snupkg, cancellationToken);

        Assert.Equal(SymbolIndexingResult.InvalidSymbolPackage, result);
        Assert.Empty(_saved);
        AssertRejectionWasExplained("lib/net8.0/Library.pdb", "no corresponding assembly");
    }

    [Fact]
    public async Task IfSnupkgPdbDoesNotDescribeCorrespondingNupkgDll_ReturnsInvalidSymbolPackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // The names line up but the PDB belongs to a different assembly, so it would serve
        // the wrong sources for the package's code.
        GivenPackageExists(TestPackage.CreateMinimal(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.dll", OtherDll } }));

        using var snupkg = TestPackage.CreateSymbols(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.pdb", LibraryPdb } });

        var result = await _target.IndexAsync(snupkg, cancellationToken);

        Assert.Equal(SymbolIndexingResult.InvalidSymbolPackage, result);
        Assert.Empty(_saved);
        AssertRejectionWasExplained("lib/net8.0/Library.pdb", "does not describe", "lib/net8.0/Library.dll");
    }

    [Fact]
    public async Task SavesPdbs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        GivenPackageExists(TestPackage.CreateMinimal(
            binaryFiles: new Dictionary<string, byte[]>
            {
                { "lib/net8.0/Library.dll", LibraryDll },
                { "lib/net8.0/Other.dll", OtherDll },
            }));

        using var snupkg = TestPackage.CreateSymbols(
            binaryFiles: new Dictionary<string, byte[]>
            {
                { "lib/net8.0/Library.pdb", LibraryPdb },
                { "lib/net8.0/Other.pdb", OtherPdb },
            });

        var result = await _target.IndexAsync(snupkg, cancellationToken);

        Assert.Equal(SymbolIndexingResult.Success, result);
        Assert.Equal(2, _saved.Count);

        AssertSaved("library.pdb", LibrarySignature, LibraryPdb);
        AssertSaved("other.pdb", OtherSignature, OtherPdb);
    }

    [Fact]
    public async Task RecordsWhichPackageVersionEachSavedPdbCameFrom()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        GivenPackageExists(TestPackage.CreateMinimal(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.dll", LibraryDll } }));
        var registry = new Mock<ISymbolRegistry>();
        var recorded = new List<SymbolReference>();
        registry
            .Setup(r => r.RecordAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<IEnumerable<SymbolReference>>(), It.IsAny<CancellationToken>()))
            .Callback((string id, NuGetVersion version, IEnumerable<SymbolReference> symbols, CancellationToken _) =>
            {
                Assert.Equal(PackageId, id);
                Assert.Equal(PackageVersion, version.ToNormalizedString());
                recorded.AddRange(symbols);
            })
            .Returns(Task.CompletedTask);
        var target = new SymbolIndexingService(_packages.Object, _packageStorage.Object, _symbols.Object, _logger, registry.Object);

        using var snupkg = TestPackage.CreateSymbols(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.pdb", LibraryPdb } });

        Assert.Equal(SymbolIndexingResult.Success, await target.IndexAsync(snupkg, cancellationToken));

        var symbol = Assert.Single(recorded);
        Assert.Equal("library.pdb", symbol.FileName);
        Assert.Equal($"{LibrarySignature:N}".ToUpperInvariant() + "ffffffff", symbol.Key);
    }

    [Fact]
    public async Task AClosedMaintenanceGateRefusesBeforeAnythingIsStored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        GivenPackageExists(TestPackage.CreateMinimal(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.dll", LibraryDll } }));
        var gate = new MaintenanceGate();
        gate.Close("test");
        var target = new SymbolIndexingService(_packages.Object, _packageStorage.Object, _symbols.Object, _logger, null, gate);

        using var snupkg = TestPackage.CreateSymbols(
            binaryFiles: new Dictionary<string, byte[]> { { "lib/net8.0/Library.pdb", LibraryPdb } });

        // A refused write is reported as such, not as an invalid symbol package.
        await Assert.ThrowsAsync<MaintenanceException>(() => target.IndexAsync(snupkg, cancellationToken));
        Assert.Empty(_saved);
    }

    [Fact]
    public async Task IfNupkgShipsTheAssemblyAsAnExecutable_SavesPdbs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // A PDB next to a tool's .exe is just as valid as one next to a library's .dll.
        GivenPackageExists(TestPackage.CreateMinimal(
            binaryFiles: new Dictionary<string, byte[]> { { "tools/Library.exe", LibraryDll } }));

        using var snupkg = TestPackage.CreateSymbols(
            binaryFiles: new Dictionary<string, byte[]> { { "tools/Library.pdb", LibraryPdb } });

        var result = await _target.IndexAsync(snupkg, cancellationToken);

        Assert.Equal(SymbolIndexingResult.Success, result);

        AssertSaved("library.pdb", LibrarySignature, LibraryPdb);
    }

    [Fact]
    public async Task IfSnupkgHasNoPdbs_ReturnsSuccessWithoutSavingAnything()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        GivenPackageExists(TestPackage.CreateMinimal());

        using var snupkg = TestPackage.CreateSymbols();

        var result = await _target.IndexAsync(snupkg, cancellationToken);

        Assert.Equal(SymbolIndexingResult.Success, result);
        Assert.Empty(_saved);
    }

    /// <summary>
    /// A rejected upload must be diagnosable: an error naming the reason has to reach the
    /// log, otherwise all the operator sees is "invalid symbol package".
    /// </summary>
    private void AssertRejectionWasExplained(params string[] expected)
    {
        var errors = _logger.Errors.ToList();

        Assert.NotEmpty(errors);
        Assert.Contains(
            errors,
            error => expected.All(text => error.Message.Contains(text, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The reason must be stated rather than left for the reader to infer from a stack
    /// trace thrown out of a parser.
    /// </summary>
    private void AssertNothingWasLoggedAsAnException()
    {
        Assert.DoesNotContain(_logger.Entries, entry => entry.Exception != null);
    }

    private void AssertSaved(string fileName, Guid signature, byte[] expectedContent)
    {
        var saved = Assert.Single(_saved, p => p.File == fileName);

        // The symbol server key is the signature GUID followed by the age, which is always
        // "ffffffff" for a portable PDB.
        // See: https://github.com/dotnet/symstore/blob/main/docs/specs/SSQP_Key_Conventions.md#portable-pdb-signature
        Assert.Equal($"{signature.ToString("N").ToUpperInvariant()}ffffffff", saved.Key);

        // The PDB is stored byte for byte, so a debugger gets back what was uploaded.
        Assert.Equal(expectedContent, saved.Content);
    }

    private void GivenPackageExists(MemoryStream nupkg)
    {
        var version = NuGetVersion.Parse(PackageVersion);

        _packages
            .Setup(p => p.FindOrNullAsync(PackageId, version, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Package { Id = PackageId, Version = version });

        _packageStorage
            .Setup(s => s.GetPackageStreamAsync(PackageId, version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nupkg);
    }

    private void GivenPackageDoesNotExist()
    {
        _packages
            .Setup(p => p.FindOrNullAsync(
                It.IsAny<string>(),
                It.IsAny<NuGetVersion>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Package?)null);
    }

    /// <summary>
    /// The header of a Windows PDB, which is what MSBuild emits for DebugType=full.
    /// </summary>
    private static byte[] WindowsPdbHeader()
    {
        return Encoding.ASCII.GetBytes("Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0");
    }

    private sealed class SavedPdb
    {
        public SavedPdb(string file, string key, byte[] content)
        {
            File = file;
            Key = key;
            Content = content;
        }

        public string File { get; }
        public string Key { get; }
        public byte[] Content { get; }
    }
}