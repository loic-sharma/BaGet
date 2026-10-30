using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Web.Tests;

/// <summary>
/// A host that serves the APIs only never calls <c>AddBaGetFrontEnd</c>, so it has no Razor Pages services:
/// mapping BaGet's endpoints must not need them.
/// </summary>
public class ApiOnlyHostTests
{
    [Fact]
    public void MapsTheEndpointsWithoutRazorPagesServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers();
        var app = builder.Build();

        var exception = Record.Exception(() => new BaGetEndpointBuilder().MapEndpoints(app));

        Assert.Null(exception);
    }

    [Fact]
    public void MapsTheEndpointsWithRazorPagesServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers();
        builder.Services.AddRazorPages();
        var app = builder.Build();

        var exception = Record.Exception(() => new BaGetEndpointBuilder().MapEndpoints(app));

        Assert.Null(exception);
    }
}
