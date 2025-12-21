using Hydra.WebApi.Middlewares;
using Microsoft.AspNetCore.Builder;

namespace Hydra.WebApi.Extensions
{
    public static class MiddlewareExtensions
    {
        public static IApplicationBuilder UseHydraContext(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<HydraContextMiddleware>();
        }
    }
}
