using Microsoft.AspNetCore.Builder;

namespace BaGet.Extensions
{
    public static class NugetBehaviorMiddlewareExtensions
    {
        public static IApplicationBuilder UseNugetBehaviorMiddleware(this IApplicationBuilder app)
        {
            return app.UseMiddleware<NugetBehaviorMiddleware>();
        }
    }
}
