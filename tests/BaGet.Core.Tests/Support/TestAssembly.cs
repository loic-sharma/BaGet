using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;

namespace BaGet.Core.Tests.Support;

/// <summary>
/// The bytes of assemblies that were really compiled, together with their real portable
/// PDBs, so symbol handling can be exercised against files a compiler produced instead of
/// hand written stand ins. The test project itself and the libraries it references are
/// built with portable PDBs, which is exactly what a .snupkg carries.
/// </summary>
public static class TestAssembly
{
    /// <summary>The compiled assembly's own bytes.</summary>
    public static byte[] ReadAssembly(Assembly assembly)
    {
        return File.ReadAllBytes(PathOf(assembly));
    }

    /// <summary>The portable PDB the assembly was compiled with.</summary>
    public static byte[] ReadPdb(Assembly assembly)
    {
        var pdbPath = Path.ChangeExtension(PathOf(assembly), ".pdb");

        if (!File.Exists(pdbPath))
        {
            throw new InvalidOperationException(
                $"{assembly.GetName().Name} was built without a portable PDB at '{pdbPath}'. " +
                "These tests need one; build with the default DebugType.");
        }

        return File.ReadAllBytes(pdbPath);
    }

    /// <summary>
    /// The debug signature GUID the assembly's CodeView debug directory entry points at,
    /// which is the GUID its portable PDB carries. Read from the assembly rather than from
    /// the PDB so that a test asserting a PDB's symbol server key has an independent source
    /// for the expected value.
    /// </summary>
    public static Guid DebugSignature(Assembly assembly)
    {
        using var stream = File.OpenRead(PathOf(assembly));
        using var peReader = new PEReader(stream);

        var codeView = peReader
            .ReadDebugDirectory()
            .Single(entry => entry.Type == DebugDirectoryEntryType.CodeView);

        return peReader.ReadCodeViewDebugDirectoryData(codeView).Guid;
    }

    private static string PathOf(Assembly assembly)
    {
        var location = assembly.Location;

        if (string.IsNullOrEmpty(location))
        {
            throw new InvalidOperationException(
                $"{assembly.GetName().Name} has no file on disk, so its bytes cannot be read.");
        }

        return location;
    }
}