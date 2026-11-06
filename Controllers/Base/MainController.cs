using Hydra.AccessManagement;
using Hydra.AccessManagement.Authorization;
using Hydra.Core;
using Hydra.DI;
using Hydra.DTOs;
using Hydra.DTOs.ViewDTOs;
using Hydra.Http;
using Hydra.IdentityAndAccess;
using Hydra.Services;
using Hydra.Services.Core;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public abstract class MainController<T> : ControllerBase where T : BaseObject<T>, new()
{
    protected IService<T> Service { get; }

    private readonly IServiceFactory _serviceFactory;
    private readonly ISessionContext _sessionContext;
    public SessionInformation? SessionInformation => _sessionContext.GetCurrent();

    /// <summary>Column-level security (Academy 5.5, layer 5).</summary>
    protected IPermissionEvaluator? PermissionEvaluator
        => _serviceFactory.GetService(typeof(IPermissionEvaluator)) as IPermissionEvaluator;

    /// <summary>Id of the signed-in caller, or null when the request is anonymous.</summary>
    protected Guid? CurrentUserId
    {
        get
        {
            var raw = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    /// <summary>
    /// Removes the columns this caller may not see — from the meta-data AND from the values.
    /// Called on every read endpoint, so an unauthorized column never reaches the wire: hiding a
    /// column in the UI while still shipping its value in the JSON is a costume, not security.
    /// </summary>
    protected async Task ApplyPropertySecurityAsync(TableDTO? table, object? item = null)
    {
        var evaluator = PermissionEvaluator;

        if (evaluator is null)
            return;

        var entityName = typeof(T).Name;

        var denied = await evaluator.GetDeniedPropertiesAsync(CurrentUserId, entityName);

        PropertySecurity.Apply(table, entityName, denied);

        if (item is not null)
            PropertySecurity.Redact(item, entityName, denied);
    }

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
    public virtual async Task<JsonResult> Create([FromBody] T entity)
    {
        var response = await Service.CreateAsync(entity);

        return new JsonResult(response) { StatusCode = response.Success ? 200 : (response.StatusCode ?? 400) };
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

        var (finalDTO, result) = await Service.GetDetailsAsync<T>(id, ResolveViewDTOType());

        await ApplyPropertySecurityAsync(finalDTO, result);

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

        await ApplyPropertySecurityAsync(table: null, item: entity);

        var response = new ResponseObject()
                         .SetActionName("GetById")
                         .SetId(id)
                         .UseDefaultMessages()
                         .SetSuccess(entity != null)
                         .SetData(entity);

        return new JsonResult(response);
    }

    /// <summary>
    /// Çoka-çok ilişkide toplu bağ kurar/koparır — Role detayındaki "Permissions" sekmesinden
    /// on izni tek seferde bağlamak gibi. Köprü tablonun adı gövdede geldiği için bu iki uç
    /// bütün ilişkileri karşılar; yeni bir ilişki için sunucuda yazılacak kod yoktur.
    ///
    /// Yetkilendirme: global filtre bu action'ları da kapsar, yani çağıran kişinin bu controller
    /// üzerinde izni yoksa istek buraya hiç ulaşmaz.
    /// </summary>
    [HttpPost]
    [Route("LinkToCollection")]
    public virtual async Task<JsonResult> LinkToCollection([FromBody] CollectionLinkDTO dto)
    {
        var response = await CollectionLinks.LinkAsync(dto);

        return new JsonResult(response) { StatusCode = response.StatusCode ?? 200 };
    }

    [HttpPost]
    [Route("UnlinkFromCollection")]
    public virtual async Task<JsonResult> UnlinkFromCollection([FromBody] CollectionLinkDTO dto)
    {
        var response = await CollectionLinks.UnlinkAsync(dto);

        return new JsonResult(response) { StatusCode = response.StatusCode ?? 200 };
    }

    private ICollectionLinkService CollectionLinks
        => (ICollectionLinkService)_serviceFactory.GetService(typeof(ICollectionLinkService));

    [HttpGet("Ping")]
    public IActionResult Ping()
    {
        return Ok("Pong");
    }

    /// <summary>
    /// Resolves the ViewDTO type for T by convention ("{T}DTO" or "{T}ViewDTO"),
    /// optionally honoring an explicit type name coming from the client.
    /// </summary>
    protected Type? ResolveViewDTOType(string? viewDTOTypeName = null)
    {
        return ViewDTOTypeResolver.Resolve(viewDTOTypeName, typeof(T).Name);
    }

    [HttpPost]
    [Route("Select")]
    public virtual async Task<JsonResult> Select([FromBody] TableDTO? tableDTO = null, [FromQuery]ViewType? viewType = ViewType.ListView)
    {
        var response = new ResponseObject()
            .SetActionName(ActionName)
            .UseDefaultMessages();

        tableDTO ??= new TableDTO();

        if (string.IsNullOrEmpty(tableDTO.Name))
            tableDTO.Name = typeof(T).Name;

        var viewDTOType = ResolveViewDTOType(tableDTO.ViewDTOTypeName);

        if (string.IsNullOrEmpty(tableDTO.ViewDTOTypeName) && viewDTOType != null)
            tableDTO.SetViewDTOTypeName(viewDTOType.Name);

        var (finalDTO, results) = await Service.SelectWithTableAsync<T>(tableDTO: tableDTO,
                                                                        viewType: viewType,
                                                                        viewDTOTypeToPrepareUsingConfigurations: viewDTOType);

        await ApplyPropertySecurityAsync(finalDTO);

        //NOTE: The TableDTO is returned directly (not wrapped in an anonymous {Table=...} package)
        //so that ApiClient.SelectAsync can deserialize ResponseObject.Data as TableDTO.
        return new JsonResult(response.SetSuccess(finalDTO != null)
                                      .SetData(finalDTO));
    }



    [HttpPut]
    [Route("Update")]
    public virtual async Task<JsonResult> Update([FromBody] T entity)
    {
        var response = await Service.UpdateAsync(entity);

        return new JsonResult(response) { StatusCode = response.Success ? 200 : (response.StatusCode ?? 400) };
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