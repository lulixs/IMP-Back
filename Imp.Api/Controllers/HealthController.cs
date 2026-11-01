using Dapper;
using Imp.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace Imp.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase {
    private readonly IDbConnectionFactory db;
    private readonly ILogger<HealthController> logger;

    public HealthController(IDbConnectionFactory db, ILogger<HealthController> logger) {
        this.db = db;
        this.logger = logger;
    }

    [HttpGet]
    public IActionResult Get() {
        return Ok(new { status = "ok" });
    }

    // GET /api/health/database. Opens a connection and runs SELECT 1.
    // 200 {"database":"ok"} if MySQL answers else 503 {"database":"error"}
    [HttpGet("database")]
    public async Task<IActionResult> GetDatabase(CancellationToken cancellationToken) {
        // Stop waiting after 5 seconds or on disconnect
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try {
            await using var connection = await db.OpenConnectionAsync(timeout.Token);
            var result = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT 1", cancellationToken: timeout.Token));

            if (result == 1) {
                return Ok(new { database = "ok" });
            }
            logger.LogError("Database health check: SELECT 1 returned {Result}", result);
        } catch (Exception ex) {
            // Log the details
            logger.LogError(ex, "Database health check failed");
        }

        return StatusCode(StatusCodes.Status503ServiceUnavailable, new { database = "error" });
    }
}
