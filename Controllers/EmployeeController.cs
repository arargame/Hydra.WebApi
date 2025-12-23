using Hydra.Core.HumanResources;
using Hydra.DI;
using Microsoft.AspNetCore.Mvc;

namespace Hydra.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeeController : MainController<Employee>
    {
        public EmployeeController(IControllerInjector controllerInjector) : base(controllerInjector)
        {
        }
    }
}
