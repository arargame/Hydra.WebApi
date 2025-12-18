using Hydra.DI;
using Hydra.WebApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace Hydra.WebApi.Controllers
{
    [Route("api/[controller]")]
    public class VesselController : MainController<Vessel>
    {
        public VesselController(IControllerInjector controllerInjector)
            : base(controllerInjector)
        {
        }
    }
}
