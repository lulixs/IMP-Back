using Dapper;
using Imp.Api.Data;
using Imp.Api.Models;
using Imp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Imp.Api.Controllers;

// Buildings, from docs/api/buildings.md. Takes the session token. Adding and removing buildings come later.
[ApiController]
[Route("api/buildings")]
[Authorize(Policy = AuthPolicies.Session)]
public class BuildingsController : ControllerBase {
    private readonly IDbConnectionFactory db;

    public BuildingsController(IDbConnectionFactory db) {
        this.db = db;
    }

    // GET /api/buildings. Every building, sorted by name.
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) {
        await using var connection = await db.OpenConnectionAsync(ct);
        var buildings = await connection.QueryAsync<BuildingSummary>(new CommandDefinition(
            "SELECT id, name FROM BUILDING ORDER BY name",
            cancellationToken: ct));
        return Ok(new { buildings });
    }
}
