using Hydra.AccessManagement;
using Hydra.DI;
using Hydra.Http;
using Hydra.IdentityAndAccess;
using Hydra.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hydra.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RoleController : MainController<Role>
    {
        private RoleService RoleService => Service as RoleService ?? throw new InvalidOperationException("Service is not RoleService");

        public RoleController(IControllerInjector injector) : base(injector) { }

        // GET: api/Role/GetUsers/{id}
        [HttpGet]
        [Route("GetUsers/{roleId}")]
        public async Task<JsonResult> GetUsers(Guid roleId)
        {
            return new JsonResult(await RoleService.GetUsersResponseAsync(roleId));
        }

        // GET: api/Role/GetPermissions/{roleId}
        [HttpGet]
        [Route("GetPermissions/{roleId}")]
        public async Task<JsonResult> GetPermissions(Guid roleId)
        {
            return new JsonResult(await RoleService.GetPermissionsResponseAsync(roleId));
        }
    }
}
