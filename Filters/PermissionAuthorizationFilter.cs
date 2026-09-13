using Hydra.AccessManagement.Authorization;
using Hydra.Core;
using Hydra.Core.Http;
using Hydra.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Hydra.WebApi.Filters
{
    /// <summary>
    /// Global, deny-by-default authorization filter.
    ///
    /// Every endpoint is closed unless something explicitly opens it:
    ///   * the endpoint carries [AllowAnonymous] (Login, health checks), or
    ///   * a Permission row matching Controller/Action is marked AllowAnonymous, or
    ///   * the caller holds a matching permission — directly or through a role, with "*" as the
    ///     superuser grant written by the Default Admin seed (Hydra Academy 5.5).
    ///
    /// On refusal the response is BOTH a real HTTP status code AND the standard Hydra envelope:
    /// the body is still a <see cref="Hydra.Http.ResponseObject"/> with Success=false and a
    /// human-readable message, now carrying StatusCode as well. Nothing about the envelope
    /// contract changes — a client that ignores status codes still sees Success=false, and a
    /// client that reads them gets 401 vs 403 properly distinguished.
    ///
    /// 401 vs 403 is decided by identity, not by permissions: we return 401 when we do not know
    /// who the caller is (signing in may fix it) and 403 when we do know and they still may not
    /// (signing in again will NOT fix it).
    ///
    /// Kill switch: "Hydra:Authorization:Enabled": false turns enforcement off for an environment
    /// that is not ready for it yet. It defaults to ON — a security control that defaults to off
    /// is not a security control.
    /// </summary>
    public class PermissionAuthorizationFilter : IAsyncAuthorizationFilter
    {
        private readonly IPermissionEvaluator _permissionEvaluator;
        private readonly ICustomConfigurationService _configuration;
        private readonly ILogService _logService;

        public PermissionAuthorizationFilter(IPermissionEvaluator permissionEvaluator,
                                     ICustomConfigurationService configuration,
                                     ILogService logService)
        {
            _permissionEvaluator = permissionEvaluator;
            _configuration = configuration;
            _logService = logService;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            // 1. [AllowAnonymous] always wins. Without this, Login itself would be unreachable
            //    and the system could never be entered at all.
            var endpoint = context.HttpContext.GetEndpoint();
            if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
                return;

            // 2. Kill switch (defaults to enabled).
            var enabledValue = _configuration.Get("Hydra:Authorization:Enabled", "true");
            if (string.Equals(enabledValue, "false", StringComparison.OrdinalIgnoreCase))
                return;

            var controller = context.RouteData.Values["controller"]?.ToString();
            var action = context.RouteData.Values["action"]?.ToString();

            var userId = ResolveUserId(context.HttpContext.User);

            // 3. [HydraAllowAuthenticated]: identity is required, but no specific permission is.
            //    Logging out must not depend on holding a "Logout" permission.
            if (endpoint?.Metadata?.GetMetadata<HydraAllowAuthenticatedAttribute>() != null)
            {
                if (userId is not null)
                    return;

                await SafeLogAsync(LogFactory.Warning("Authorization", "PermissionDenied",
                    $"401 for {controller}/{action} (anonymous caller on an authenticated-only endpoint)"));

                context.Result = BuildResult(401, controller, action);
                return;
            }

            PermissionDecision decision;

            try
            {
                decision = await _permissionEvaluator.EvaluateEndpointAsync(userId, controller, action);
            }
            catch (Exception ex)
            {
                // A failure while DECIDING is not a grant. Fail closed, and make the reason loud:
                // silently allowing traffic because the permission store hiccuped is exactly how
                // authorization systems end up not being authorization systems.
                await SafeLogAsync(LogFactory.Error($"Permission evaluation failed for {controller}/{action}: {ex.Message}"));

                context.Result = BuildResult(500, controller, action);
                return;
            }

            if (decision.Allowed)
                return;

            var statusCode = userId is null ? 401 : 403;

            await SafeLogAsync(LogFactory.Warning("Authorization", "PermissionDenied",
                $"{statusCode} for {controller}/{action} (user: {userId?.ToString() ?? "anonymous"}) - {decision.Reason}"));

            context.Result = BuildResult(statusCode, controller, action);
        }

        private static Guid? ResolveUserId(ClaimsPrincipal? principal)
        {
            if (principal?.Identity?.IsAuthenticated != true)
                return null;

            var raw = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(raw, out var userId) ? userId : null;
        }

        private static JsonResult BuildResult(int statusCode, string? controller, string? action)
        {
            var response = ResponseFactory.FromStatus(
                statusCode,
                detail: $"{controller}/{action}",
                actionName: action);

            // JsonResult carries the envelope in the body; StatusCode puts the same number on the
            // wire, so proxies, logs and the browser's network tab all agree with the payload.
            return new JsonResult(response) { StatusCode = statusCode };
        }

        private async Task SafeLogAsync(Log log)
        {
            try
            {
                await _logService.SaveAsync(log, LogRecordType.Console);
            }
            catch
            {
                // Logging must never turn a refusal into a crash.
            }
        }
    }
}
