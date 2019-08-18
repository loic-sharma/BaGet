using System.Threading.Tasks;
using BaGet.Core.Indexing;
using BaGet.Core.Metadata;
using BaGet.Core.Mirror;
using BaGet.Protocol;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BaGet.Core.Tests.Mirror
{
    public class MirrorServiceTests
    {
        public class FindPackageVersionsOrNullAsync : FactsBase
        {
            [Fact]
            public async Task MergesLocalAndUpstream()
            {
                await Task.Yield();
            }
        }

        public class FindPackagesOrNullAsync : FactsBase
        {
            [Fact]
            public async Task MergesLocalAndUpstream()
            {
                await Task.Yield();
            }
        }

        public class MirrorAsync : FactsBase
        {
            [Fact]
            public async Task SkipsIfAlreadyMirrored()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task SkipsIfUpstreamDoesntHavePackage()
            {
                await Task.Yield();
            }

            [Fact]
            public async Task MirrorsPackage()
            {
                await Task.Yield();
            }
        }

        public class FactsBase
        {
            private readonly Mock<IPackageService> _packages;
            private readonly Mock<IPackageContentResource> _content;
            private readonly Mock<IPackageMetadataResource> _metadata;
            private readonly Mock<IPackageIndexingService> _indexer;

            private readonly MirrorService _target;

            public FactsBase()
            {
                _packages = new Mock<IPackageService>();
                _content = new Mock<IPackageContentResource>();
                _metadata = new Mock<IPackageMetadataResource>();
                _indexer = new Mock<IPackageIndexingService>();

                _target = new MirrorService(
                    _packages.Object,
                    _content.Object,
                    _metadata.Object,
                    _indexer.Object,
                    Mock.Of<ILogger<MirrorService>>());
            }
        }
    }
}
