using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Core
{
    public interface IPlugin
    {
        void Configure(IServiceCollection services, IConfiguration configuration);
    }
}
