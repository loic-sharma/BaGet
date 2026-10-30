using System;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace BaGet.Storage.Tests;

/// <summary>
/// The object-store emulators the provider tests run against (B2 of the Artifactory-parity plan): Azurite for Azure Blob, S3Mock for
/// S3 and a Cloud Storage emulator for Google Cloud Storage. Each starts once per run; a test that needs one skips (<see cref="Assert.Skip(string)"/>)
/// when there is no Docker daemon, as in <c>BaGet.Database.Tests</c>. Aliyun OSS has no emulator.
/// </summary>
public static class StorageContainers
{
    // Pinned: the emulators' behaviour (multipart, conditional writes, resumable sessions) is what the tests exercise.
    public const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.35.0";
    // MinIO no longer publishes images (neither Docker Hub nor quay.io answer a pull); Adobe's S3Mock implements the multipart,
    // copy-part and conditional-write calls the S3 provider uses.
    public const string S3MockImage = "adobe/s3mock:4.7.0";
    // fake-gcs-server (fsouza) was tried first: it finalises a resumable session on the "bytes */*" status query, which the
    // protocol defines as a pure query, so it cannot stand in for the real service. This emulator follows the protocol.
    public const string GcsEmulatorImage = "oittaa/gcp-storage-emulator:latest";

    public const string S3AccessKey = "test";
    public const string S3SecretKey = "test";

    // The well-known development account of the Azure emulators.
    public const string AzuriteAccount = "devstoreaccount1";
    public const string AzuriteKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    private static readonly Lazy<Task<IContainer>> Azurite = new(() => StartAsync(
        new ContainerBuilder(AzuriteImage)
            .WithCommand("azurite-blob", "--blobHost", "0.0.0.0", "--skipApiVersionCheck", "--loose")
            .WithPortBinding(10000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("successfully listens"))
            .Build()));

    private static readonly Lazy<Task<IContainer>> S3Mock = new(() => StartAsync(
        new ContainerBuilder(S3MockImage)
            .WithPortBinding(9090, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Started S3MockApplication"))
            .Build()));

    private static readonly Lazy<Task<IContainer>> GcsEmulator = new(() => StartAsync(
        new ContainerBuilder(GcsEmulatorImage)
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(8080))
            .Build()));

    /// <summary>The blob endpoint of Azurite, <c>http://127.0.0.1:port/devstoreaccount1</c>.</summary>
    public static async Task<string> AzuriteBlobEndpointAsync()
    {
        var container = await Guard(() => Azurite.Value, "Azurite");
        return $"http://127.0.0.1:{container.GetMappedPublicPort(10000)}/{AzuriteAccount}";
    }

    /// <summary>The address of S3Mock, <c>http://127.0.0.1:port</c>.</summary>
    public static async Task<string> S3UrlAsync()
    {
        var container = await Guard(() => S3Mock.Value, "S3Mock");
        return $"http://127.0.0.1:{container.GetMappedPublicPort(9090)}";
    }

    /// <summary>The address of the Cloud Storage emulator, <c>http://127.0.0.1:port</c>.</summary>
    public static async Task<string> GcsUrlAsync()
    {
        var container = await Guard(() => GcsEmulator.Value, "Cloud Storage");
        return $"http://127.0.0.1:{container.GetMappedPublicPort(8080)}";
    }

    private static async Task<IContainer> StartAsync(IContainer container)
    {
        await container.StartAsync().ConfigureAwait(false);
        return container;
    }

    private static async Task<T> Guard<T>(Func<Task<T>> start, string what)
    {
        try
        {
            return await start().ConfigureAwait(false);
        }
        catch (Exception e) when (IsDockerUnavailable(e))
        {
            Assert.Skip($"The {what} emulator needs Docker, which is not available: {e.GetBaseException().Message}");
            throw;
        }
    }

    private static bool IsDockerUnavailable(Exception e)
    {
        for (var inner = e; inner != null; inner = inner.InnerException)
        {
            if ((inner.GetType().FullName ?? string.Empty).Contains("Docker", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
