using Hydra.DI;
using Hydra.IdentityAndAccess;
using Hydra.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hydra.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PermissionController : MainController<Permission>
    {
        private PermissionService PermissionService => Service as PermissionService
          ?? throw new InvalidOperationException("Service is not PermissionService");
        public PermissionController(IControllerInjector injector) : base(injector) { }

        // GET: api/Permission/GetUsers/{permissionId}
        [HttpGet]
        [Route("GetUsers/{permissionId}")]
        public async Task<JsonResult> GetUsers(Guid permissionId)
        {
            var response = await PermissionService.GetUsersResponseAsync(permissionId);
            return new JsonResult(response);
        }

        // GET: api/Permission/GetRoles/{permissionId}
        [HttpGet]
        [Route("GetRoles/{permissionId}")]
        public async Task<JsonResult> GetRoles(Guid permissionId)
        {
            var response = await PermissionService.GetRolesResponseAsync(permissionId);
            return new JsonResult(response);
        }
    }
}
