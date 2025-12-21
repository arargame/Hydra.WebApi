using Hydra.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Hydra.WebApi.Middlewares
{
    public class HydraContextMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly Guid? _platformId;
        private const string CorrelationIdHeader = "X-Correlation-ID";

        public HydraContextMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            
            var platformIdString = configuration["Hydra:PlatformId"];
            if (Guid.TryParse(platformIdString, out var parsedId))
            {
                _platformId = parsedId;
            }
        }

        public async Task Invoke(HttpContext context)
        {
            // 1. Determine CorrelationId
            Guid correlationId;
            if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out StringValues headerCorrelationId) && 
                Guid.TryParse(headerCorrelationId, out var parsedCorrelationId))
            {
                correlationId = parsedCorrelationId;
            }
            else
            {
                correlationId = Guid.NewGuid();
            }

            // 2. Set Context
            HydraContext.Set(correlationId, _platformId);
            
            Console.WriteLine($"[HydraContext] Middleware Set: CorrelationId={correlationId}, PlatformId={_platformId}");

            // 3. Add to Response Headers for client tracking
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[CorrelationIdHeader] = correlationId.ToString();
                return Task.CompletedTask;
            });

            try
            {
                await _next(context);
            }
            finally
            {
                // 4. Cleanup to prevent thread pollution (though AsyncLocal is usually safe, good practice)
                HydraContext.Clear();
            }
        }
    }
}
