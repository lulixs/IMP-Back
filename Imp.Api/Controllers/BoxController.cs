

using Imp.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Imp.Api.Data;
using Dapper;
using Imp.Api.Models;
using Imp.Api.Errors;
using System.Text.RegularExpressions;

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

    // GET /api/boxes/{id}. {id:int} means a box number that isn't a number never reaches this method:
    // the framework answers 404, as for any address that doesn't exist.
    [HttpGet("{id:int}")]
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
        
        string? catSearch = search;
        string? idPrefix = search;
        if (!string.IsNullOrEmpty(search)) {
            if (idPrefix![0] == '#') idPrefix = idPrefix.Substring(1);

            if (int.TryParse(idPrefix, out _)) {
                idPrefix += "%";
                boxStructureListQuery += " AND CAST(b.id AS CHAR) LIKE @idPrefix";
            } else {
                // LOCATE, not LIKE, so _ and % in the search are plain characters instead of wildcards.
                // It ignores capitals the same way, through the column's collation.
                boxStructureListQuery += " AND LOCATE(@catSearch, c.name) > 0";
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
            new {today, idPrefix, catSearch, catId, isRacked}, cancellationToken: ct
        ));

        return Ok(new { boxes = boxList.Select(Box.FromRow) });
    }

    // POST /api/boxes
    [HttpPost]
    public async Task<IActionResult> AddBox(NewBoxRequest request, CancellationToken ct) {
        if (request.CategoryID == null || request.CategoryID < 0) 
            throw ApiError.BadRequest("Category cannot be null.");

        if (request.BuildingID == null || request.BuildingID < 0) 
            throw ApiError.BadRequest("Building cannot be null.");

        if (request.ShelfLetter == null || !Regex.IsMatch(request.ShelfLetter, @"^[A-Z]{1,3}\z")) 
            throw ApiError.BadRequest("Shelf letter must be one to three capital letters.");
        
        if (request.ShelfLevel is not (1 or 2 or 3))
            throw ApiError.BadRequest("Shelf level must be 1, 2, or 3.");

        if (request.Fullness is not ("FULL" or "HALF" or "EMPTY"))
            throw ApiError.BadRequest("Fullness must be FULL, HALF, or EMPTY.");

        DateOnly date;
        if (!DateOnly.TryParseExact(request.RackedSince, "yyyy-MM-dd", out date)) {
            throw ApiError.BadRequest("Racked since must be of the form yyyy-MM-dd.");
        }

        if (date > DateOnly.FromDateTime(StoreClock.Today())) {
            throw ApiError.BadRequest("The storage date cannot be in the future.");
        }

        int employeeId = User.GetEmployeeId()!.Value;
        int deviceId = User.GetDeviceId()!.Value;
        await using var connection = await db.OpenConnectionAsync(ct);

        await using var transaction = await connection.BeginTransactionAsync(ct);

        bool categoryExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM CATEGORY WHERE id = @categoryId)",
            new { categoryId = request.CategoryID }, cancellationToken: ct, transaction: transaction
        ));

        if (!categoryExists)
            throw ApiError.BadRequest("That category no longer exists. Choose another.");
        
        bool buildingExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM BUILDING WHERE id = @buildingId)",
            new { buildingId = request.BuildingID }, cancellationToken: ct, transaction: transaction
        ));

        if (!buildingExists)
            throw ApiError.BadRequest("That building no longer exists. Choose another.");
        
        bool isElevated = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT is_elevated FROM DEVICE WHERE id = @deviceId",
            new { deviceId }, cancellationToken: ct, transaction: transaction
        ));

        // Notes are trimmed, and blank becomes null (boxes.md).
        string? notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        int boxId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO BOX (category_id, building_id, shelf_letter, shelf_level, 
                fullness, notes, racked_since, created_by, created_at)
            VALUES (@catId, @buildId, @letter, @level, @fullness, @notes, @date, 
                @employeeId, UTC_TIMESTAMP());
            SELECT LAST_INSERT_ID();
            """,
            new { catId = request.CategoryID, buildId = request.BuildingID, letter = request.ShelfLetter, 
                level = request.ShelfLevel, fullness = request.Fullness, notes,
                date = date.ToDateTime(TimeOnly.MinValue), employeeId},
            cancellationToken: ct, transaction: transaction
        ));

        // BOX ADDED, labeled with the box number. ADDED has no details (boxes.md).
        await AuditLog.WriteAsync(connection, new AuditEntry {
            EmployeeId = employeeId,
            IsElevated = isElevated,
            DeviceId = deviceId,
            ActionType = AuditAction.ADDED,
            TargetType = AuditTarget.BOX,
            TargetId = boxId,
            TargetLabel = $"#{boxId}",
        }, transaction, ct);

        await transaction.CommitAsync(ct);

        // Reply with the new box in the full box shape, read the same way GetBox does.
        var row = await connection.QuerySingleAsync<BoxSummary>(new CommandDefinition(
            boxStructureQuery + " WHERE b.id = @id",
            new { id = boxId, today = StoreClock.Today() }, cancellationToken: ct
        ));

        // 201 Created, with a Location header pointing to GET /api/boxes/{id}.
        return CreatedAtAction(nameof(GetBox), new { id = boxId }, Box.FromRow(row));
    }
}
