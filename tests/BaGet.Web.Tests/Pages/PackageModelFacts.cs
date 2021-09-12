using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Moq;
using Xunit;

namespace BaGet.Web.Tests
{
    public class PackageModelFacts
    {
        private readonly Mock<IMirrorService> _mirror;
        private readonly Mock<IUrlGenerator> _url;
        private readonly PackageModel _target;

        private readonly CancellationToken _cancellation = CancellationToken.None;

        public PackageModelFacts()
        {
            _mirror = new Mock<IMirrorService>();
            _url = new Mock<IUrlGenerator>();
            _target = new PackageModel(
                _mirror.Object,
                _url.Object);
        }

        [Fact]
        public async Task ReturnsNotFound()
        {
            _mirror
                .Setup(m => m.FindPackagesAsync("testpackage", _cancellation))
                .ReturnsAsync(new List<Package>());

            await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

            Assert.False(_target.Found);
            Assert.Equal("testpackage", _target.Package.Id);
            Assert.Null(_target.DependencyGroups);
            Assert.Null(_target.Versions);
        }

        [Fact]
        public async Task ReturnsRequestedVersion()
        {
            _mirror
                .Setup(m => m.FindPackagesAsync("testpackage", _cancellation))
                .ReturnsAsync(new List<Package>
                {
                    CreatePackage("1.0.0"),
                    CreatePackage("2.0.0"),
                    CreatePackage("3.0.0"),
                });

            await _target.OnGetAsync("testpackage", "2.0.0", _cancellation);

            Assert.True(_target.Found);
            Assert.Equal("testpackage", _target.Package.Id);
            Assert.Equal("2.0.0", _target.Package.NormalizedVersionString);
        }

        private Package CreatePackage(string version)
        {
            return new Package
            {
                Id = "testpackage",
                NormalizedVersionString = version,
                Dependencies = new List<PackageDependency>(),
                PackageTypes = new List<PackageType>()
            };
        }
    }
}
