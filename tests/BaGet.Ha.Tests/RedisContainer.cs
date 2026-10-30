using System;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace BaGet.Ha.Tests;

/// <summary>
/// Starts one real Redis on first use and shares it for the whole run. When Docker is not available the tests skip instead of
/// failing, so the normal suite stays green without it (as in BaGet.Database.Tests and BaGet.Telemetry.Tests).
/// </summary>
public static class RedisContainer
{
    private const string Image = "redis:7.4-alpine";
    private const int Port = 6379;

    private static readonly SemaphoreSlimAsync Gate = new();
    private static IContainer? _container;
    private static string? _skipReason;

    /// <summary>The connection string of the shared Redis; skips the test when there is no Docker daemon.</summary>
    public static async Task<string> ConnectionStringAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_skipReason != null) Assert.Skip(_skipReason);
            if (_container == null)
            {
                try
                {
                    var container = new ContainerBuilder(Image)
                        .WithPortBinding(Port, true)
                        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
                        .Build();
                    await container.StartAsync();
                    _container = container;
                }
                catch (Exception e)
                {
                    _skipReason = "Docker is not available: " + e.GetType().Name;
                    Assert.Skip(_skipReason);
                }
            }

            return $"{_container!.Hostname}:{_container.GetMappedPublicPort(Port)}";
        }
        finally
        {
            Gate.Release();
        }
    }

    private sealed class SemaphoreSlimAsync
    {
        private readonly System.Threading.SemaphoreSlim _semaphore = new(1, 1);

        public Task WaitAsync() => _semaphore.WaitAsync();

        public void Release() => _semaphore.Release();
    }
}
