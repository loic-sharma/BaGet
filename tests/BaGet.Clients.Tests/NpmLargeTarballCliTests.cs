using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Clients.Tests;

/// <summary>Collection that runs alone: a test that counts the process's allocations cannot share it with tests running in parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationMeasurements
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Allocation measurements";
}

/// <summary>A 40 MB tarball through the real npm: intact, and without the server holding it in memory.</summary>
[Collection(AllocationMeasurements.Name)]
public class NpmLargeTarballCliTests
{
    [Fact]
    public async Task ALargeTarballIsPublishedAndInstalledIntactWithBoundedServerMemory()
    {
        var (server, node) = await NpmRegistryCliTests.StartAsync(new Dictionary<string, string> { ["ApiKey"] = NpmRegistryCliTests.GlobalKey });
        await using var _ = server;

        // 40 MB of random bytes do not compress, so the request body is about 53 MB of base64.
        var before = GC.GetTotalAllocatedBytes(precise: false);
        var publish = await NpmRegistryCliTests.RunAsync(node, NpmRegistryCliTests.Npmrc(server, NpmRegistryCliTests.GlobalKey) + NpmRegistryCliTests.Package("pub", "big-pkg", "1.0.0")
            + "head -c 41943040 /dev/urandom > payload.bin\nsha256sum payload.bin | cut -d' ' -f1 > /tmp/expected-big.sha\nnpm publish 2>&1\necho \"--- sha\"\ncat /tmp/expected-big.sha\n");
        var allocated = GC.GetTotalAllocatedBytes(precise: false) - before;
        Assert.True(publish.ExitCode == 0, publish.All);
        var expected = publish.Stdout.Split("--- sha\n").Last().Trim();

        // Reading the body, the base64 and the tarball does not allocate in proportion to the file.
        Assert.True(allocated < 24L * 1024 * 1024, $"the server allocated {allocated} bytes while a 40 MB tarball was published");

        var consume = await NpmRegistryCliTests.RunAsync(node, NpmRegistryCliTests.Npmrc(server, null) + """
            mkdir app && cd app
            echo '{"name":"app","version":"1.0.0"}' > package.json
            npm install big-pkg
            sha256sum node_modules/big-pkg/payload.bin | cut -d' ' -f1
            """);
        Assert.True(consume.ExitCode == 0, consume.All);
        Assert.Equal(expected, consume.Stdout.Trim().Split('\n').Last());
        Assert.True((await server.PackageAsync(NpmRegistryCliTests.Repository, "big-pkg"))!.Versions.Single().TarballSize > 41943040L);
    }
}
