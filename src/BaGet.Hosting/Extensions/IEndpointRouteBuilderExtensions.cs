using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace BaGet
{
    public static class EndpointRoutingExtensions
    {
        public static IEndpointConventionBuilder WithRouteName(this IEndpointConventionBuilder endpoints, string name)
        {
            return endpoints.WithMetadata(
                new EndpointNameMetadata(name),
                new RouteNameMetadata(name));
        }
    }
}
