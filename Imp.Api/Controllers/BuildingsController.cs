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

    // GET /api/buildings. Every building, sorted by name, with how many active and soft-deleted boxes use it.
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) {
        await using var connection = await db.OpenConnectionAsync(ct);
        var buildings = await connection.QueryAsync<BuildingListItem>(new CommandDefinition("""
            SELECT
                bu.id, bu.name,
                COUNT(CASE WHEN b.id IS NOT NULL AND b.deleted_at IS NULL THEN 1 END) AS active_box_count,
                COUNT(b.deleted_at) AS deleted_box_count
            FROM BUILDING AS bu
            LEFT JOIN BOX AS b ON b.building_id = bu.id
            GROUP BY bu.id
            ORDER BY bu.name
            """,
            cancellationToken: ct));
        return Ok(new { buildings });
    }
}
