using Hydra.Core;
using Hydra.DI;
using Microsoft.AspNetCore.Mvc;

namespace Hydra.WebApi.Controllers
{
    /// <summary>
    /// Log tablosu için okuma ucu. GenericDetailsView'ın otomatik "Loglar" sekmesi buraya
    /// (EntityType + EntityId filtreleriyle) Select atar.
    ///
    /// NOT: Select, EF DbSet'i değil TableService/QueryBuilder yolunu kullanır; ancak
    /// MainController'ın bağımlılık zinciri Repository&lt;Log&gt; üzerinden gittiği için
    /// HydraDbContext'te DbSet&lt;Log&gt; tanımlı olmalıdır (şu an yorum satırında).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class LogController : MainController<Log>
    {
        public LogController(IControllerInjector controllerInjector) : base(controllerInjector)
        {
        }
    }
}
