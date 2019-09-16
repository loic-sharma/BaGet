using Xunit;

namespace BaGet.Protocol.Tests
{
    public class NuGetClientTests : IClassFixture<ProtocolFixture>
    {
        private readonly NuGetClient _target;

        public NuGetClientTests(ProtocolFixture fixture)
        {
            _target = fixture.NuGetClient;
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
