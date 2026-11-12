using Microsoft.AspNetCore.Mvc;

namespace TodoApp.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    // Liveness only; database readiness will be added with persistence.
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "healthy" });
}
