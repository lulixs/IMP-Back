

using Imp.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Imp.Api.Data;
using Dapper;
using Imp.Api.Models;
using Imp.Api.Errors;
using System.Diagnostics.Eventing.Reader;

namespace Imp.Api.Controllers;

[ApiController]
[Route("api/boxes")]
[Authorize(Policy = AuthPolicies.Session)]
public class BoxController : ControllerBase
{
    private readonly IDbConnectionFactory db;

    private const string boxStructureQuery = """
        SELECT 
            b.*, 
            c.name as cat_name, c.symbol as cat_symbol,
            bu.name as building_name,
            cb.name as created_by_name,
            ub.name as last_updated_by_name,
            db.name as deleted_by_name,
            DATEDIFF(@today, b.racked_since) as days_stored,
            b.is_racked AND (DATEDIFF(@today, b.racked_since) >= m.alert_threshold_days) as stored_too_long 
        FROM BOX as b
            JOIN CATEGORY as c ON b.category_id = c.id
            JOIN BUILDING as bu ON b.building_id = bu.id
            JOIN EMPLOYEE_ACCOUNT as cb ON b.created_by = cb.id
            LEFT JOIN EMPLOYEE_ACCOUNT as ub ON b.last_updated_by = ub.id
            LEFT JOIN EMPLOYEE_ACCOUNT as db ON b.deleted_by = db.id
            CROSS JOIN MASTER_ACCOUNT as m
    """;

    public BoxController(IDbConnectionFactory db) {
        this.db = db;
    }

    // GET /api/boxes/{box id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBox(int id, CancellationToken ct) {
        await using var connection = await db.OpenConnectionAsync(ct);
        var today = StoreClock.Today();
        
        var box = await connection.QuerySingleOrDefaultAsync<BoxSummary>(new CommandDefinition(
            boxStructureQuery + " WHERE b.id = @id", 
            new {id, today}, cancellationToken: ct
        ));

        if (box == null) throw ApiError.NotFound("This box doesn't exist.");
        return Ok(Box.FromRow(box));
    }

    // GET /api/boxes
    [HttpGet]
    public async Task<IActionResult> GetBoxes([FromQuery] string? search, 
        [FromQuery] string? categoryId, [FromQuery] string? racked, 
        [FromQuery] string? sort, CancellationToken ct) {
        
        string boxStructureListQuery = boxStructureQuery + " WHERE b.deleted_at IS NULL";
        
        string? catPattern = search;
        string? idPrefix = search;
        if (!string.IsNullOrEmpty(search)) {
            if (idPrefix![0] == '#') idPrefix = idPrefix.Substring(1);

            if (int.TryParse(idPrefix, out _)) {
                idPrefix += "%";
                boxStructureListQuery += " AND CAST(b.id AS CHAR) LIKE @idPrefix";
            } else {
                catPattern = "%" + catPattern + "%";
                boxStructureListQuery += " AND c.name LIKE @catPattern";
            }
        }

        int? catId = null;
        if (categoryId != null) {
            if (!int.TryParse(categoryId, out int parsed)) 
                throw ApiError.BadRequest("Category ID must be a number.");
            catId = parsed;
            boxStructureListQuery += " AND b.category_id = @catId";
        }
        bool? isRacked = null;
        if (racked != null) {
            if (!bool.TryParse(racked, out bool parsed))
                throw ApiError.BadRequest("Racked must be true or false.");
            isRacked = parsed;
            boxStructureListQuery += " AND b.is_racked = @isRacked";
        }
        if (sort != null){
            if (sort == "oldest") 
                boxStructureListQuery += " ORDER BY days_stored DESC, b.id";
            else if (sort == "newest") 
                boxStructureListQuery += " ORDER BY days_stored ASC, b.id";
            else throw ApiError.BadRequest("Sort must be oldest or newest.");
        } else {
            boxStructureListQuery += " ORDER BY days_stored DESC, b.id";
        }
        await using var connection = await db.OpenConnectionAsync(ct);
        var today = StoreClock.Today();

        var boxList = await connection.QueryAsync<BoxSummary>(new CommandDefinition(
            boxStructureListQuery,
            new {today, idPrefix, catPattern, catId, isRacked}, cancellationToken: ct
        ));

        return Ok(new { boxes = boxList.Select(Box.FromRow) });
    }
}




