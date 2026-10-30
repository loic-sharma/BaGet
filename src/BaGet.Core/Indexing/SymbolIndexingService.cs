﻿﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace BaGet.Core;

// Based off: https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery/Services/SymbolPackageUploadService.cs
// Based off: https://github.com/NuGet/NuGet.Jobs/blob/master/src/Validation.Symbols/SymbolsValidatorService.cs#L44
public class SymbolIndexingService : ISymbolIndexingService
{
    // Entry paths inside a package are matched case-insensitively, so an extension
    // must be too: a symbol package carrying "Library.PDB" is valid.
    private static readonly HashSet<string> ValidSymbolPackageContentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdb",
        ".nuspec",
        ".xml",
        ".psmdcp",
        ".rels",
        ".p7s"
    };

    // The extensions of the files a PDB can describe, most likely first.
    private static readonly string[] AssemblyExtensions = { ".dll", ".exe" };

    // "BSJB": the signature at the start of the ECMA-335 metadata root, which is what a
    // portable PDB is. A Windows PDB starts with "Microsoft C/C++ MSF 7.00" instead, and
    // a file that is not a PDB at all starts with something else again.
    private static readonly byte[] PortablePdbSignature = { 0x42, 0x53, 0x4A, 0x42 };

    // The length of a portable PDB's debug metadata id: a 16 byte GUID and a 4 byte stamp.
    private const int DebugMetadataIdLength = 20;

    private readonly IPackageDatabase _packages;
    private readonly IPackageStorageService _packageStorage;
    private readonly ISymbolStorageService _storage;
    private readonly ILogger<SymbolIndexingService> _logger;
    private readonly ISymbolRegistry? _registry;
    private readonly MaintenanceGate? _gate;

    // registry: records which package version each PDB came from, so they can be listed and deleted per
    // version (null records nothing). gate: checked before any PDB is stored, so a refused write leaves no file.
    public SymbolIndexingService(
        IPackageDatabase packages,
        IPackageStorageService packageStorage,
        ISymbolStorageService storage,
        ILogger<SymbolIndexingService> logger,
        ISymbolRegistry? registry = null,
        MaintenanceGate? gate = null)
    {
        _registry = registry;
        _gate = gate;
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _packageStorage = packageStorage ?? throw new ArgumentNullException(nameof(packageStorage));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SymbolIndexingResult> IndexAsync(Stream? stream, CancellationToken cancellationToken)
    {
        try
        {
            using (var symbolPackage = new PackageArchiveReader(stream, leaveStreamOpen: true))
            {
                var pdbPaths = await GetSymbolPackagePdbPathsOrNullAsync(symbolPackage, cancellationToken);
                if (pdbPaths == null)
                {
                    return SymbolIndexingResult.InvalidSymbolPackage;
                }

                // Ensure a corresponding NuGet package exists.
                var packageId = symbolPackage.NuspecReader.GetId();
                var packageVersion = symbolPackage.NuspecReader.GetVersion();

                var package = await _packages.FindOrNullAsync(packageId, packageVersion, includeUnlisted: true, cancellationToken);
                if (package == null)
                {
                    _logger.LogWarning(
                        "Rejecting symbol package {PackageId} {PackageVersion}: the package it belongs to does not exist",
                        packageId,
                        packageVersion.ToNormalizedString());
                    return SymbolIndexingResult.PackageNotFound;
                }

                using (var pdbs = new PdbList())
                {
                    // Extract the portable PDBs from the snupkg. Nothing is persisted until after all
                    // PDBs have been extracted and validated sucessfully.
                    foreach (var pdbPath in pdbPaths)
                    {
                        var portablePdb = await ExtractPortablePdbOrNullAsync(symbolPackage, pdbPath, cancellationToken);
                        if (portablePdb == null)
                        {
                            return SymbolIndexingResult.InvalidSymbolPackage;
                        }

                        pdbs.Add(portablePdb);
                    }

                    // Every PDB must describe a file the package itself ships, otherwise the
                    // symbol server would serve symbols for code that is not in the package.
                    if (!await PdbsDescribePackageAsync(packageId, packageVersion, pdbs, cancellationToken))
                    {
                        return SymbolIndexingResult.InvalidSymbolPackage;
                    }

                    _gate?.ThrowIfClosed();

                    // Persist the portable PDBs to storage.
                    foreach (var pdb in pdbs)
                    {
                        pdb.Content.Position = 0;
                        await _storage.SavePortablePdbContentAsync(pdb.Filename, pdb.Key, pdb.Content, cancellationToken);
                    }

                    // PDBs are stored by file and key, not by package: remember where these came from.
                    if (_registry != null && pdbs.Count > 0)
                    {
                        await _registry.RecordAsync(
                            packageId,
                            packageVersion,
                            pdbs.Select(p => new SymbolReference(p.Filename, p.Key)),
                            cancellationToken);
                    }

                    return SymbolIndexingResult.Success;
                }
            }
        }
        catch (MaintenanceException)
        {
            // A refused write is not a malformed package.
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to index symbol package due to exception");
            return SymbolIndexingResult.InvalidSymbolPackage;
        }
    }

    private async Task<IReadOnlyList<string>?> GetSymbolPackagePdbPathsOrNullAsync(
        PackageArchiveReader symbolPackage,
        CancellationToken cancellationToken)
    {
        try
        {
            await symbolPackage.ValidatePackageEntriesAsync(cancellationToken);
        }
        catch (PackagingException e)
        {
            // An entry whose path escapes the extraction directory, such as "../../evil.pdb",
            // or that is otherwise unusable. Say so: otherwise the only trace a rejected
            // upload leaves is an "invalid symbol package" with no reason attached.
            _logger.LogError(e, "Rejecting symbol package: its entries are not safe to extract");
            return null;
        }

        var files = (await symbolPackage.GetFilesAsync(cancellationToken)).ToList();

        // Ensure there are no unexpected file extensions within the symbol package.
        var invalidFiles = files.Where(file => !IsValidSymbolFile(file)).ToList();
        if (invalidFiles.Count > 0)
        {
            _logger.LogError(
                "Rejecting symbol package: the entries {InvalidEntries} do not have an extension a symbol package may contain",
                string.Join(", ", invalidFiles));
            return null;
        }

        return files.Where(IsPdbPath).ToList();
    }

    internal static bool IsPdbPath(string path)
    {
        return string.Equals(Path.GetExtension(path), ".pdb", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsValidSymbolFile(string entry)
    {
        if (string.IsNullOrEmpty(Path.GetFileName(entry))) return false;

        var extension = Path.GetExtension(entry);
        if (string.IsNullOrEmpty(extension)) return false;

        return ValidSymbolPackageContentExtensions.Contains(extension);
    }

    internal static bool AreSymbolFilesValid(IReadOnlyList<string> entries)
    {
        return entries.All(IsValidSymbolFile);
    }

    private async Task<PortablePdb?> ExtractPortablePdbOrNullAsync(
        PackageArchiveReader symbolPackage,
        string pdbPath,
        CancellationToken cancellationToken)
    {
        Stream? pdbStream = null;
        PortablePdb? result = null;

        try
        {
            using (var rawPdbStream = await symbolPackage.GetStreamAsync(pdbPath, cancellationToken))
            {
                pdbStream = await rawPdbStream.AsTemporaryFileStreamAsync(cancellationToken);

                // A symbol package may only carry portable PDBs. A Windows PDB cannot be read
                // as metadata and cannot be keyed for the symbol server, so reject it here
                // with a reason rather than letting the metadata reader throw.
                if (!HasPortablePdbSignature(pdbStream))
                {
                    _logger.LogError(
                        "Rejecting symbol package: the entry {PdbPath} is not a portable PDB",
                        pdbPath);
                    return null;
                }

                var signature = ReadPortablePdbSignatureOrNull(pdbStream, pdbPath);
                if (signature == null)
                {
                    return null;
                }

                var fileName = Path.GetFileName(pdbPath).ToLowerInvariant();
                var key = $"{signature.Value.ToString("N").ToUpperInvariant()}ffffffff";

                pdbStream.Position = 0;
                result = new PortablePdb(pdbPath, fileName, key, signature.Value, pdbStream);
            }
        }
        finally
        {
            if (result == null)
            {
                pdbStream?.Dispose();
            }
        }

        return result;
    }

    /// <summary>
    /// Whether the stream starts with the signature every portable PDB starts with.
    /// </summary>
    internal static bool HasPortablePdbSignature(Stream pdbStream)
    {
        if (pdbStream == null) throw new ArgumentNullException(nameof(pdbStream));
        if (!pdbStream.CanSeek) throw new ArgumentException("The stream must be seekable", nameof(pdbStream));

        var position = pdbStream.Position;

        try
        {
            pdbStream.Position = 0;

            var signature = new byte[PortablePdbSignature.Length];
            var read = pdbStream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);

            // A file shorter than the signature - including an empty one - is not a PDB.
            return read == signature.Length && signature.SequenceEqual(PortablePdbSignature);
        }
        finally
        {
            pdbStream.Position = position;
        }
    }

    private Guid? ReadPortablePdbSignatureOrNull(Stream pdbStream, string pdbPath)
    {
        pdbStream.Position = 0;

        try
        {
            using (var pdbReaderProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream, MetadataStreamOptions.LeaveOpen))
            {
                var reader = pdbReaderProvider.GetMetadataReader();

                // Metadata that starts with the right signature but carries no debug metadata
                // header is not a PDB, it is an assembly's metadata.
                var header = reader.DebugMetadataHeader;
                if (header == null || header.Id.Length != DebugMetadataIdLength)
                {
                    _logger.LogError(
                        "Rejecting symbol package: the entry {PdbPath} does not contain portable PDB debug metadata",
                        pdbPath);
                    return null;
                }

                return new BlobContentId(header.Id).Guid;
            }
        }
        catch (BadImageFormatException e)
        {
            _logger.LogError(e, "Rejecting symbol package: the entry {PdbPath} is a malformed portable PDB", pdbPath);
            return null;
        }
    }

    /// <summary>
    /// Whether every PDB describes a file shipped by the package it claims to belong to.
    /// See: https://github.com/NuGet/NuGet.Jobs/blob/master/src/Validation.Symbols/SymbolsValidatorService.cs#L170
    /// </summary>
    private async Task<bool> PdbsDescribePackageAsync(
        string packageId,
        NuGetVersion packageVersion,
        IReadOnlyList<PortablePdb> pdbs,
        CancellationToken cancellationToken)
    {
        if (pdbs.Count == 0)
        {
            return true;
        }

        using (var packageStream = await _packageStorage.GetPackageStreamAsync(packageId, packageVersion, cancellationToken))
        using (var package = new PackageArchiveReader(packageStream, leaveStreamOpen: true))
        {
            var packageFiles = (await package.GetFilesAsync(cancellationToken)).ToList();

            foreach (var pdb in pdbs)
            {
                var assemblyPath = FindAssemblyPathOrNull(packageFiles, pdb.Path);
                if (assemblyPath == null)
                {
                    _logger.LogError(
                        "Rejecting symbol package {PackageId} {PackageVersion}: its PDB {PdbPath} has no corresponding assembly in the package",
                        packageId,
                        packageVersion.ToNormalizedString(),
                        pdb.Path);
                    return false;
                }

                if (!await AssemblyMatchesPdbAsync(package, assemblyPath, pdb, cancellationToken))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// A symbol package mirrors the layout of the package it belongs to, so the assembly a
    /// PDB describes sits at the same path with an assembly extension.
    /// </summary>
    private static string? FindAssemblyPathOrNull(IReadOnlyList<string> packageFiles, string pdbPath)
    {
        foreach (var extension in AssemblyExtensions)
        {
            var candidate = NormalizePath(Path.ChangeExtension(pdbPath, extension));

            foreach (var packageFile in packageFiles)
            {
                if (string.Equals(NormalizePath(packageFile), candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return packageFile;
                }
            }
        }

        return null;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private async Task<bool> AssemblyMatchesPdbAsync(
        PackageArchiveReader package,
        string assemblyPath,
        PortablePdb pdb,
        CancellationToken cancellationToken)
    {
        using (var rawAssemblyStream = await package.GetStreamAsync(assemblyPath, cancellationToken))
        using (var assemblyStream = await rawAssemblyStream.AsTemporaryFileStreamAsync(cancellationToken))
        {
            IReadOnlyList<Guid> signatures;

            try
            {
                signatures = ReadCodeViewSignatures(assemblyStream);
            }
            catch (BadImageFormatException e)
            {
                _logger.LogError(
                    e,
                    "Rejecting symbol package: the package's {AssemblyPath} is not a readable assembly, so its PDB {PdbPath} cannot be matched to it",
                    assemblyPath,
                    pdb.Path);
                return false;
            }

            if (signatures.Count == 0)
            {
                // The assembly ships without a debug directory, so there is nothing to match
                // the PDB against. Index it rather than reject what cannot be checked.
                _logger.LogWarning(
                    "The package's {AssemblyPath} has no debug directory, so its PDB {PdbPath} cannot be matched to it",
                    assemblyPath,
                    pdb.Path);
                return true;
            }

            if (!signatures.Contains(pdb.Signature))
            {
                _logger.LogError(
                    "Rejecting symbol package: its PDB {PdbPath} does not describe the package's {AssemblyPath}",
                    pdb.Path,
                    assemblyPath);
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// The debug signatures an assembly's CodeView debug directory entries point at. A
    /// portable PDB describes the assembly when it carries one of them.
    /// </summary>
    private static IReadOnlyList<Guid> ReadCodeViewSignatures(Stream assemblyStream)
    {
        assemblyStream.Position = 0;

        using (var peReader = new PEReader(assemblyStream, PEStreamOptions.LeaveOpen))
        {
            return peReader
                .ReadDebugDirectory()
                .Where(entry => entry.Type == DebugDirectoryEntryType.CodeView)
                .Select(entry => peReader.ReadCodeViewDebugDirectoryData(entry).Guid)
                .ToList();
        }
    }

    private class PortablePdb : IDisposable
    {
        public PortablePdb(string path, string filename, string key, Guid signature, Stream content)
        {
            Path = path;
            Filename = filename;
            Key = key;
            Signature = signature;
            Content = content;
        }

        /// <summary>The PDB's path within the symbol package.</summary>
        public string Path { get; }
        public string Filename { get; }
        public string Key { get; }

        /// <summary>The PDB's debug signature GUID.</summary>
        public Guid Signature { get; }
        public Stream Content { get; }

        public void Dispose() => Content.Dispose();
    }

    private class PdbList : List<PortablePdb>, IDisposable
    {
        public void Dispose()
        {
            foreach (var pdb in this)
            {
                pdb.Dispose();
            }
        }
    }
}