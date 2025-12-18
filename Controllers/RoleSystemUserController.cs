using Hydra.AccessManagement;
using Hydra.DI;
using Microsoft.AspNetCore.Mvc;

namespace Hydra.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RoleSystemUserController : MainController<RoleSystemUser>
    {
        public RoleSystemUserController(IControllerInjector injector) : base(injector) { }
    }
}
