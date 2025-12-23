using Hydra.Core;
using Hydra.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Hydra.WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ClientLogController : ControllerBase
    {
        private readonly ILogService _logService;

        public ClientLogController(ILogService logService)
        {
            _logService = logService;
        }

        [HttpPost]
        public async Task<IActionResult> LogError([FromBody] ClientLogRequest request)
        {
            var log = LogFactory.Error(
                description: BuildDescription(request),
                processType: LogProcessType.Read);
            
            log.SetCategory("ClientError");
            // SetName is on BaseObject (Log), but SetCategory returns ILog. 
            // So we access Name property directly or cast, but explicit property is safer.
            log.Name = request.Url ?? "HttpClient";

            // CorrelationId and PlatformId will be auto-populated by LogService from HydraContext
            // But we can override with client-provided values if needed
            if (!string.IsNullOrEmpty(request.CorrelationId) && Guid.TryParse(request.CorrelationId, out var correlationId))
            {
                log.SetCorrelationId(correlationId);
            }

            if (request.PlatformId != Guid.Empty)
            {
                log.SetPlatformId(request.PlatformId);
            }

            await _logService.SaveAsync(log, LogRecordType.Database);

            return Ok();
        }

        private string BuildDescription(ClientLogRequest request)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Message: {request.Message}");

            if (request.StatusCode.HasValue)
                sb.AppendLine($"Status Code: {request.StatusCode}");

            if (!string.IsNullOrEmpty(request.Url))
                sb.AppendLine($"URL: {request.Url}");

            if (!string.IsNullOrEmpty(request.StackTrace))
            {
                sb.AppendLine();
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(request.StackTrace);
            }

            sb.AppendLine($"Client Timestamp: {request.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");

            return sb.ToString();
        }
    }

    public class ClientLogRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? StackTrace { get; set; }
        public string? Url { get; set; }
        public int? StatusCode { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
        public Guid PlatformId { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
