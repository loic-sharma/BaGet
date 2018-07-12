using System;
using System.Collections.Generic;
using Xunit;
using BaGet.Core.Configuration;
using BaGet.Extensions;
using BaGet.Services.Mirror.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Services.Mirror.Tests
{
    public class ServiceCollectionExtensionsTest
    {

        [Fact]
        public void AskServiceProviderForNotConfiguredMirrorOptions()
        {
            ServiceProvider provider = new ServiceCollection()
                .AddBaGetContext() //Required
                .AddMirrorServices() //Method Under Test!
                .BuildServiceProvider();

            InvalidOperationException expected = Assert.Throws<InvalidOperationException>(
                () => provider.GetRequiredService<IMirrorService>()
            );
            Assert.Contains(nameof(BaGetOptions.Mirror), expected.Message);
        }

        [Fact]
        public void AskServiceProviderForWellConfiguredMirrorOptions()
        {
            //Create a IConfiguration with a minimal "Mirror" object.
            KeyValuePair<string, string> initialData = new KeyValuePair<string, string>($"{nameof(BaGetOptions.Mirror)}:{nameof(MirrorOptions.EnableReadThroughCaching)}",false.ToString());
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new KeyValuePair<string, string>[] { initialData }).Build();

            ServiceProvider provider = new ServiceCollection()
                .Configure<BaGetOptions>(configuration)
                .AddMirrorServices() //Method Under Test!
                .BuildServiceProvider();

            Assert.NotNull(provider.GetRequiredService<IMirrorService>());
        }

    }
}
