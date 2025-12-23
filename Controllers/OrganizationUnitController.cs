using Hydra.Core.HumanResources;
using Hydra.DI;
using Microsoft.AspNetCore.Mvc;

namespace Hydra.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrganizationUnitController : MainController<OrganizationUnit>
    {
        public OrganizationUnitController(IControllerInjector controllerInjector) : base(controllerInjector)
        {
        }
    }
}
