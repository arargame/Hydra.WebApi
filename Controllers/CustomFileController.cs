using Hydra.DI;
using Hydra.DTOs;
using Hydra.FileOperations;
using Hydra.Http;
using Hydra.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace Hydra.WebApi.Controllers
{
    /// <summary>
    /// CustomFile inherits the generic CRUD/Select surface from MainController&lt;T&gt; like
    /// every other entity, plus two endpoints a raw byte[] entity needs that the generic
    /// contract doesn't cover on its own:
    ///
    ///   - Upload: accepts one or more files (as raw bytes in a JSON body, via FileUploadDTO)
    ///     and creates one CustomFile per item, attached to EntityType/EntityId.
    ///   - Download: returns an actual HTTP file response (Content-Type + Content-Disposition),
    ///     not JSON. GET /Get/{id} would return CustomFile.Data as a base64 string inside the
    ///     usual JSON envelope, which a browser shows as text rather than downloading.
    ///
    /// See Hydra/Academy/03-FileManagement-Review/01-current-state-audit.md, finding #2 — this
    /// closes it. Adapted from a working upload/download pair in the GedenLines codebase
    /// (WebApi.GedenLines/Controllers/CustomFileController.cs), reshaped to Hydra's own
    /// MainController&lt;T&gt;/Service&lt;T&gt;/ResponseObject conventions.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class CustomFileController : MainController<CustomFile>
    {
        private CustomFileService CustomFileService => Service as CustomFileService
            ?? throw new InvalidOperationException("Service is not CustomFileService");

        public CustomFileController(IControllerInjector injector) : base(injector) { }

        // POST api/CustomFile/Upload
        [HttpPost]
        [Route("Upload")]
        public async Task<JsonResult> Upload([FromBody] FileUploadDTO uploadDTO)
        {
            var response = await CustomFileService.UploadAsync(uploadDTO);

            return new JsonResult(response);
        }

        // GET api/CustomFile/Download/{id} — a real file response, not an envelope.
        [HttpGet]
        [Route("Download/{id:guid}")]
        public async Task<IActionResult> Download(Guid id)
        {
            var file = await Service.GetByIdAsync(id);

            if (file?.Data == null)
                return NotFound();

            new FileExtensionContentTypeProvider().TryGetContentType(file.FullName, out var contentType);

            return File(file.Data, contentType ?? "application/octet-stream", file.FullName);
        }

        // GET api/CustomFile/ByEntity/{entityType}/{entityId} — everything attached to one entity.
        [HttpGet]
        [Route("ByEntity/{entityType}/{entityId}")]
        public async Task<JsonResult> ByEntity(string entityType, string entityId)
        {
            var files = await CustomFileService.GetByEntityAsync(entityType, entityId);

            var response = new ResponseObject()
                                .SetActionName("ByEntity")
                                .UseDefaultMessages()
                                .SetSuccess(true)
                                .SetData(files);

            return new JsonResult(response);
        }
    }
}
