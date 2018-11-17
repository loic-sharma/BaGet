using System.Net.Http;
using System.Threading.Tasks;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class RegistrationClientTests
    {
        private readonly RegistrationClient _target;

        public RegistrationClientTests()
        {
            var httpClient = new HttpClient();
            _target = new RegistrationClient(httpClient);
        }

        [Fact]
        public async Task GetsNewtonsoftJsonRegistrationIndex()
        {
            var url = "https://api.nuget.org/v3/registration3/newtonsoft.json/index.json";

            var result = await _target.GetRegistrationIndexOrNullAsync(url);

            Assert.NotNull(result);
            Assert.Equal(1, result.Pages.Count);
            Assert.True(result.Pages[0].Count >= 63);
            Assert.True(result.Pages[0].ItemsOrNull.Count >= 63);
            Assert.Equal(new NuGetVersion("3.5.8"), result.Pages[0].Lower);
            Assert.Equal(new NuGetVersion("12.0.1-beta1"), result.Pages[0].Upper);
        }

        [Fact]
        public async Task GetsFakeRegistrationIndex()
        {
            var url = "https://api.nuget.org/v3/registration3/fake/index.json";

            var result = await _target.GetRegistrationIndexOrNullAsync(url);

            Assert.NotNull(result);
            Assert.True(result.Pages.Count >= 27);
            Assert.Null(result.Pages[0].ItemsOrNull);
            Assert.Equal(64, result.Pages[0].Count);
            Assert.Equal(new NuGetVersion("1.0.0-alpha-10"), result.Pages[0].Lower);
            Assert.Equal(new NuGetVersion("1.66.1"), result.Pages[0].Upper);
        }

        [Fact]
        public async Task GetsFakeRegistrationPage()
        {
            var url = "https://api.nuget.org/v3/registration3/fake/index.json";

            var index = await _target.GetRegistrationIndexOrNullAsync(url);
            var result = await _target.GetRegistrationIndexPageAsync(index.Pages[0].PageUrl);

            Assert.NotNull(result);
            Assert.Equal(64, result.Count);
            Assert.Equal(new NuGetVersion("1.0.0-alpha-10"), result.Lower);
            Assert.Equal(new NuGetVersion("1.66.1"), result.Upper);
        }

        [Fact]
        public async Task GetsNewtonsoftRegistrationLeaf()
        {
            var url = "https://api.nuget.org/v3/registration3/newtonsoft.json/index.json";

            var index = await _target.GetRegistrationIndexOrNullAsync(url);
            var leaf = await _target.GetRegistrationLeafAsync(index.Pages[0].ItemsOrNull[0].LeafUrl);

            Assert.Equal(url, leaf.RegistrationIndexUrl);
        }

        [Fact]
        public async Task GetFakeRegistrationLeaf()
        {
            var url = "https://api.nuget.org/v3/registration3/fake/index.json";

            var index = await _target.GetRegistrationIndexOrNullAsync(url);
            var page = await _target.GetRegistrationIndexPageAsync(index.Pages[0].PageUrl);
            var leaf = await _target.GetRegistrationLeafAsync(page.ItemsOrNull[0].LeafUrl);

            Assert.Equal(url, leaf.RegistrationIndexUrl);
        }
    }
}
