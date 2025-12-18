using Hydra.DI;
using Hydra.AccessManagement;
using Microsoft.AspNetCore.Mvc;

namespace Hydra.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RolePermissionController : MainController<RolePermission>
    {
        public RolePermissionController(IControllerInjector injector) : base(injector) { }
    }
}
