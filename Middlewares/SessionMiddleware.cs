using Hydra.CacheManagement.Managers;
using Hydra.IdentityAndAccess;
using Hydra.AccessManagement;
using Microsoft.AspNetCore.Http;

namespace Hydra.WebApi.Middlewares
{
    public class SessionMiddleware
    {
        private readonly RequestDelegate _next;

        public SessionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context,
                                     ISessionContext sessionContext,
                                    SessionInformationCacheManager cacheManager)
        {
            if (context.Request.Headers.TryGetValue("X-User-Id", out var userIdHeader)
                && Guid.TryParse(userIdHeader, out var userId))
            {
                if (cacheManager.TryGetSession(userId, out var session))
                {
                    session!.Ip = context.Connection.RemoteIpAddress?.ToString();
                    session.UserAgent = context.Request.Headers["User-Agent"].ToString();

                    session.SetLastActiviyTime();

                    sessionContext.Set(session);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Session expired or invalid.");
                    return;
                }
            }
            else if (context.Request.Headers.ContainsKey("X-System-Request"))
            {
                sessionContext.Set(new SessionInformation
                {
                    SystemUserId = Guid.Empty,
                    Name = "System",
                    Ip = context.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = context.Request.Headers["User-Agent"].FirstOrDefault()
                }.SetLastActiviyTime());
            }

            await _next(context);
            sessionContext.Clear();
        }
    }


}
