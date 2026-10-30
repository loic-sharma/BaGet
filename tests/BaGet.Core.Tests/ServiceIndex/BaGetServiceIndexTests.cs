using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace BaGet.Core.Tests.ServiceIndex;

public class BaGetServiceIndexTests
{
    private readonly Mock<IUrlGenerator> _url;
    private readonly BaGetServiceIndex _target;

    public BaGetServiceIndexTests()
    {
        _url = new Mock<IUrlGenerator>();
        _url.Setup(u => u.GetPackagePublishResourceUrl()).Returns("https://test/api/v2/package");
        _url.Setup(u => u.GetSymbolPublishResourceUrl()).Returns("https://test/api/v2/symbol");
        _url.Setup(u => u.GetSearchResourceUrl()).Returns("https://test/v3/search");
        _url.Setup(u => u.GetPackageMetadataResourceUrl()).Returns("https://test/v3/registration");
        _url.Setup(u => u.GetPackageContentResourceUrl()).Returns("https://test/v3/package");
        _url.Setup(u => u.GetAutocompleteResourceUrl()).Returns("https://test/v3/autocomplete");
        _url.Setup(u => u.GetPackageDetailsUriTemplate()).Returns("https://test/packages/{id}/{version}");
        _url.Setup(u => u.GetReportAbuseUriTemplate()).Returns("https://test/packages/{id}/{version}/report-abuse");
        _url.Setup(u => u.GetVulnerabilityIndexUrl()).Returns("https://test/v3/vulnerabilities/index.json");

        _target = new BaGetServiceIndex(_url.Object);
    }

    private async Task<string[]?>? Types()
    {
        var response = await _target.GetAsync(CancellationToken.None);

        #pragma warning disable CS8619
        return response.Resources?.Select(r => r.Type).ToArray();
        #pragma warning restore CS8619
    }

    private async Task<string?> UrlOf(string type)
    {
        var response = await _target.GetAsync(CancellationToken.None);

        return response.Resources?.Single(r => r.Type == type).ResourceUrl;
    }

    [Fact]
    public async Task ATemplateTheRepositoryCannotServeIsLeftOut()
    {
        _url.Setup(u => u.GetPackageDetailsUriTemplate()).Returns((string?)null);
        _url.Setup(u => u.GetReportAbuseUriTemplate()).Returns((string?)null);

        var types = (await Types()!)!;

        Assert.DoesNotContain(types, t => t!.StartsWith("PackageDetailsUriTemplate"));
        Assert.DoesNotContain(types, t => t!.StartsWith("ReportAbuseUriTemplate"));
        Assert.Contains("SearchQueryService", types);
    }

    [Fact]
    public async Task TheVersionIsThree()
    {
        var response = await _target.GetAsync(CancellationToken.None);

        Assert.Equal("3.0.0", response.Version);
    }

    [Theory]
    [InlineData("PackagePublish/2.0.0")]
    [InlineData("SymbolPackagePublish/4.9.0")]
    [InlineData("SearchQueryService")]
    [InlineData("SearchQueryService/3.0.0-beta")]
    [InlineData("SearchQueryService/3.0.0-rc")]
    [InlineData("RegistrationsBaseUrl")]
    [InlineData("RegistrationsBaseUrl/3.0.0-beta")]
    [InlineData("RegistrationsBaseUrl/3.0.0-rc")]
    [InlineData("PackageBaseAddress/3.0.0")]
    [InlineData("SearchAutocompleteService")]
    [InlineData("SearchAutocompleteService/3.0.0-beta")]
    [InlineData("SearchAutocompleteService/3.0.0-rc")]
    public async Task AdvertisesTheResource(string type)
    {
        #pragma warning disable CS8602, CS8604
        Assert.Contains(type, await Types());
        #pragma warning restore CS8602, CS8604
    }

    [Theory]
    [InlineData("PackageDetailsUriTemplate/5.1.0")]
    [InlineData("ReportAbuseUriTemplate/3.0.0-beta")]
    [InlineData("ReportAbuseUriTemplate/3.0.0-rc")]
    public async Task AdvertisesTheUriTemplate(string type)
    {
        // These are the @type strings nuget.org publishes. Note report abuse has no
        // "3.0.0" form: only the beta and rc ones.
        #pragma warning disable CS8602, CS8604
        Assert.Contains(type, await Types());
        #pragma warning restore CS8602, CS8604
    }

    [Fact]
    public async Task DoesNotAdvertiseAReportAbuseVersionNuGetOrgDoesNot()
    {
        #pragma warning disable CS8602, CS8604
        Assert.DoesNotContain("ReportAbuseUriTemplate/3.0.0", await Types());
        Assert.DoesNotContain("ReportAbuseUriTemplate", await Types());
        #pragma warning restore CS8602, CS8604
    }

    [Fact]
    public async Task TheUriTemplatesKeepTheirPlaceholders()
    {
        // A client expands these itself, so the braces must survive verbatim and
        // must not be percent-encoded.
        var details = await UrlOf("PackageDetailsUriTemplate/5.1.0");
        var reportAbuse = await UrlOf("ReportAbuseUriTemplate/3.0.0-rc");

        Assert.Contains("{id}", details);
        Assert.Contains("{version}", details);
        Assert.Contains("{id}", reportAbuse);
        Assert.Contains("{version}", reportAbuse);
        Assert.DoesNotContain("%7B", details);
        Assert.DoesNotContain("%7B", reportAbuse);
    }

    [Fact]
    public async Task BothReportAbuseVersionsShareOneUrl()
    {
        Assert.Equal(
            await UrlOf("ReportAbuseUriTemplate/3.0.0-beta"),
            await UrlOf("ReportAbuseUriTemplate/3.0.0-rc"));
    }

    [Fact]
    public async Task EveryResourceHasATypeAndAUrl()
    {
        var response = await _target.GetAsync(CancellationToken.None);

        #pragma warning disable CS8604
        Assert.All(response.Resources, resource =>
        #pragma warning restore CS8604
        {
            Assert.False(string.IsNullOrEmpty(resource.Type));
            Assert.False(string.IsNullOrEmpty(resource.ResourceUrl));
        });
    }
}