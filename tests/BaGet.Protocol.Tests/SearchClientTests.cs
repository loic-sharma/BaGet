using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class SearchClientTests : IClassFixture<ProtocolFixture>
    {
        private readonly SearchClient _target;

        public SearchClientTests(ProtocolFixture fixture)
        {
            _target = fixture.SearchClient;
        }

        // TODO: Test search
        // TODO: Test autocomplete
    }
}
