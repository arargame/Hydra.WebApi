using Hydra.Core.HumanResources;
using Hydra.DI;
using Microsoft.AspNetCore.Mvc;

namespace Hydra.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PositionController : MainController<Position>
    {
        public PositionController(IControllerInjector controllerInjector) : base(controllerInjector)
        {
        }
    }
}
