using Hydra.AccessManagement;
using Hydra.DI;
using Hydra.IdentityAndAccess;
using Microsoft.AspNetCore.Mvc;

namespace Hydra.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SystemUserPermissionController : MainController<SystemUserPermission>
    {
        public SystemUserPermissionController(IControllerInjector injector) : base(injector) { }
    }
}
