using Microsoft.AspNetCore.Mvc;

namespace RagApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Basic liveness check. Returns 200 if the API is running.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() =>
        Ok(new
        {
            status    = "healthy",
            timestamp = DateTime.UtcNow,
            version   = "1.0.0"
        });
}
