using Xunit;

namespace BaGet.Protocol.Tests
{
    public class NuGetClientFactoryTests : IClassFixture<ProtocolFixture>
    {
        private readonly NuGetClientFactory _target;

        public NuGetClientFactoryTests(ProtocolFixture fixture)
        {
            _target = fixture.NuGetClientFactory;
        }

        // TODO: Test package download
        // TODO: Test package manifest download
        // TODO: List package versions
        // TODO: List package versions #2
        // TODO: List all package versions
        // TODO: Get package metadata inlined
        // TODO: Get package metadata paged
        // TODO: Get single package metadata inlined
        // TODO: Get single package metadata paged
        // TODO: Search
        // TODO: Autocomplete
    }
}
