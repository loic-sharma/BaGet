using System.Linq;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Web.Tests;

/// <summary>
/// This test project is built with the plain SDK, so its entry assembly carries no generated
/// [ApplicationPart] attributes: like a worker-service host, MVC sees only what BaGet registers.
/// </summary>
public class ApplicationPartsTests
{
    [Theory]
    [InlineData("BaGet.Web.V3")]
    [InlineData("BaGet.Web.V2")]
    [InlineData("BaGet.Catalog")]
    [InlineData("BaGet.Validation")]
    [InlineData("BaGet.Identity.Api")]
    public void RegistersBaGetAssemblyWithoutEntryAssemblyAttributes(string assemblyName)
    {
        var manager = Manager();

        Assert.Contains(manager.ApplicationParts, part => part.Name == assemblyName);
    }

    [Theory]
    [InlineData("BaGet.Frontend.Base")]
    [InlineData("BaGet.Identity.FrontEnd")]
    [InlineData("BaGet.Web.FrontEnd")]
    public void AddBaGetFrontEndRegistersTheScreenAssembliesAndTheApiRegistrationDoesNot(string assemblyName)
    {
        Assert.DoesNotContain(Manager().ApplicationParts, part => part.Name == assemblyName);

        var services = new ServiceCollection();
        services.AddControllers().AddBaGetApplicationParts();
        services.AddBaGetFrontEnd();

        Assert.Contains(Manager(services).ApplicationParts, part => part.Name == assemblyName);
    }

    [Fact]
    public void RegistersEachAssemblyOnce()
    {
        var services = new ServiceCollection();
        services.AddControllers().AddBaGetApplicationParts();
        services.AddControllers().AddBaGetApplicationParts();

        var parts = Manager(services).ApplicationParts
            .Where(part => part is AssemblyPart)
            .Select(part => part.Name)
            .ToList();

        Assert.Equal(parts.Distinct().Count(), parts.Count);
    }

    private static ApplicationPartManager Manager(IServiceCollection? services = null)
    {
        if (services == null)
        {
            services = new ServiceCollection();
            services.AddControllers().AddBaGetApplicationParts();
        }

        return (ApplicationPartManager)services
            .Single(descriptor => descriptor.ServiceType == typeof(ApplicationPartManager))
            .ImplementationInstance!;
    }
}
