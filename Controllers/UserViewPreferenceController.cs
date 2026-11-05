using Hydra.Core.Preferences;
using Hydra.DI;
using Microsoft.AspNetCore.Mvc;

namespace Hydra.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserViewPreferenceController : MainController<UserViewPreference>
    {
        public UserViewPreferenceController(IControllerInjector injector) : base(injector)
        {
        }
    }
}
