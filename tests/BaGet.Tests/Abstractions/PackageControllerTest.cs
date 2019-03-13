using System;
using System.Threading.Tasks;
using BaGet.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace BaGet.Tests.Abstractions
{
    public abstract class PackageControllerTest 
    {
        private readonly string IndexUrlFormatString = "v3/package/{0}/index.json";
        protected readonly BaGetServerFixture Fixture;
        protected readonly ITestOutputHelper Helper;

        public PackageControllerTest(ITestOutputHelper helper, BaGetServerFixture fixture) //: base(helper)
        {
            Fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
            Helper = helper ?? throw new ArgumentNullException(nameof(helper));
        }

        [Theory]
        [InlineData("id01")]
        [InlineData("id02")]
        public async Task AskEmptyServerForNotExistingPackageID(string packageID)
        {
            //Ask Empty Storage for a not existings ID
            var response = await Fixture.HttpClient.GetAsync(string.Format(IndexUrlFormatString, packageID));
            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}

