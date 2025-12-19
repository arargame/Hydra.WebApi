using Hydra.AccessManagement;
using Hydra.Core;
using Hydra.DI;
using Hydra.DTOs;
using Hydra.DTOs.ViewDTOs;
using Hydra.Http;
using Hydra.IdentityAndAccess;
using Hydra.Services.Core;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public abstract class MainController<T> : ControllerBase where T : BaseObject<T>, new()
{
    protected IService<T> Service { get; }

    private readonly IServiceFactory _serviceFactory;
    private readonly ISessionContext _sessionContext;
    public SessionInformation? SessionInformation => _sessionContext.GetCurrent();

    public string? Name
    {
        get
        {
            return ControllerContext.RouteData.Values["controller"]?.ToString(); 
        }
    }

    public string? ActionName
    {
        get
        {
            return ControllerContext.RouteData.Values["action"]?.ToString();
        }
    }


    protected MainController(IControllerInjector injector)
    {
        _serviceFactory = injector.ServiceFactory;
        _sessionContext = injector.SessionContext;

        var serviceType = typeof(IService<>).MakeGenericType(typeof(T));

        Service = _serviceFactory.GetService(serviceType) as IService<T>
            ?? throw new InvalidOperationException($"Service for {typeof(T).Name} not found");
    }

    [HttpPost]
    [Route("Create")]
    public async Task<JsonResult> Create([FromBody] T entity)
    {
        var response = await Service.CreateAsync(entity);

        return new JsonResult(response);
    }

    [HttpDelete]
    [Route("Delete/{id:guid}")]
    public async Task<JsonResult> Delete(Guid id)
    {
        var response = await Service.DeleteAsync(id);
        return new JsonResult(response);
    }

    [HttpPost]
    [Route("DeleteBulk")]
    public async Task<JsonResult> DeleteBulk([FromBody] List<Guid> idList)
    {
        var response = await Service.DeleteRangeAsync(idList);
        return new JsonResult(response);
    }

    [HttpGet]
    [Route("Details/{id:guid}")]
    public async Task<JsonResult> Details(Guid id)
    {
        var response = new ResponseObject()
                            .SetActionName("Details")
                            .SetId(id)
                            .UseDefaultMessages();

        var (finalDTO, result) = await Service.GetDetailsAsync<T>(id);

        var dataPackage = new
        {
            Table = finalDTO,
            Item = result
        };

        return new JsonResult(
            response.SetData(dataPackage)
                    .SetSuccess(result != null)
        );
    }



    //GET /api/exam/456f89ab-1234-43dd-9ccc-1a2b3c4d5e6f?getAllIncludes=false&includes=Category&includes=Questions

    [HttpGet("Get/{id:guid}")]
    public async Task<JsonResult> Get(Guid id, bool getAllIncludes = false, [FromQuery] params string[] includes)
    {
        includes = getAllIncludes ? Service.GetAllIncludes() : includes;

        var entity = await Service.GetByIdAsync(id, getAllIncludes, includes);

        var response = new ResponseObject()
                         .SetActionName("GetById")
                         .SetId(id)
                         .UseDefaultMessages()
                         .SetSuccess(entity != null)
                         .SetData(entity);

        return new JsonResult(response);
    }

    [HttpGet("Ping")]
    public IActionResult Ping()
    {
        return Ok("Pong");
    }

    [HttpPost]
    [Route("Select")]
    public async Task<JsonResult> Select([FromBody] TableDTO? tableDTO = null, [FromQuery]ViewType? viewType = ViewType.ListView)
    {
        var response = new ResponseObject()
            .SetActionName(ActionName)
            .UseDefaultMessages();

        var (finalDTO, results) = await Service.SelectWithTableAsync<T>(tableDTO: tableDTO, viewType: viewType);

        var dataPackage = new
        {
            Table = finalDTO
        };

        return new JsonResult(response.SetSuccess(results?.Any() ?? false)
                                      .SetData(dataPackage));
    }



    [HttpPut]
    [Route("Update")]
    public async Task<JsonResult> Update([FromBody] T entity)
    {
        var response = await Service.UpdateAsync(entity);

        return new JsonResult(response);
    }

    [HttpPut]
    [Route("UpdateBulk")]
    public async Task<JsonResult> UpdateBulk([FromBody] List<T> entities)
    {
        var response = await Service.UpdateBulkAsync(entities);

        return new JsonResult(response);
    }
#if DEBUG
    [HttpPost("Seed/{count:int}")]
    public virtual async Task<IActionResult> Seed(int count)
    {
        await Service.SeedAsync(count);
        return Ok($"{count} generated.");
    }
#endif
}