using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Mooc.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ErrorsController : ControllerBase
    {
        private readonly ILogger<ErrorsController> _logger;

        public ErrorsController(ILogger<ErrorsController> logger)
        {
            _logger = logger;
        }

        [HttpPost("client")]
        public IActionResult LogClientError([FromBody] ClientErrorModel error)
        {
            try
            {
                _logger.LogError("Erreur côté client: {Type} - {Message} - URL: {Url} - UserAgent: {UserAgent}", 
                    error.Type, error.Message, error.Url, error.UserAgent);

                return Ok(new { success = true, logged = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du logging d'erreur client");
                return StatusCode(500, new { error = "Erreur serveur" });
            }
        }
    }

    public class ClientErrorModel
    {
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string UserId { get; set; } = string.Empty;
        public int ErrorCount { get; set; }
    }
}