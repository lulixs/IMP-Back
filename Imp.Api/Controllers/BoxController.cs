

using Imp.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Imp.Api.Data;
using Dapper;
using Imp.Api.Models;
using Imp.Api.Errors;
using System.Data.Common;
using System.Text.Json;
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

        var box = await LoadBoxAsync(connection, id, ct);

        if (box == null) throw ApiError.NotFound("This box doesn't exist.");
        return Ok(box);
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
        CheckFields(request);

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

        await CategoryNameAsync(connection, transaction, request.CategoryID!.Value, ct);
        await BuildingNameAsync(connection, transaction, request.BuildingID!.Value, ct);
        bool isElevated = await IsElevatedAsync(connection, transaction, deviceId, ct);
        string? notes = CleanNotes(request.Notes);

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

        // no details for ADDED
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

        // 201 + Location header
        return CreatedAtAction(nameof(GetBox), new { id = boxId }, (await LoadBoxAsync(connection, boxId, ct))!);
    }

    // PUT /api/boxes/{id}. saves every field, incl. rack/unrack (D-170)
    [HttpPut("{id:int}")]
    public async Task<IActionResult> SaveBox(int id, SaveBoxRequest request, CancellationToken ct) {
        CheckFields(request);

        if (request.IsRacked == null)
            throw ApiError.BadRequest("Racked must be true or false.");

        if (request.ResetRackedSince == null)
            throw ApiError.BadRequest("Reset racked since must be true or false.");

        int employeeId = User.GetEmployeeId()!.Value;
        int deviceId = User.GetDeviceId()!.Value;
        await using var connection = await db.OpenConnectionAsync(ct);

        await using var transaction = await connection.BeginTransactionAsync(ct);

        // lock box row till commit, so simultaneous saves queue up
        var old = await connection.QuerySingleOrDefaultAsync<BoxSummary>(new CommandDefinition(
            """
            SELECT b.*, c.name AS cat_name, bu.name AS building_name
            FROM BOX AS b
                JOIN CATEGORY AS c ON b.category_id = c.id
                JOIN BUILDING AS bu ON b.building_id = bu.id
            WHERE b.id = @id
            FOR UPDATE OF b
            """,
            new { id }, transaction, cancellationToken: ct
        ));

        if (old == null) throw ApiError.NotFound("This box doesn't exist.");
        if (old.DeletedAt != null) throw ApiError.NotFound("This box was deleted.");

        // stale copy (D-174)
        if (!SameTime(request.LastUpdatedAt, old.LastUpdatedAt))
            throw ApiError.Conflict("Someone else changed this box. Reload it and make your change again.");

        // names for audit details
        string categoryName = await CategoryNameAsync(connection, transaction, request.CategoryID!.Value, ct);
        string buildingName = await BuildingNameAsync(connection, transaction, request.BuildingID!.Value, ct);

        string letter = request.ShelfLetter!;
        int level = request.ShelfLevel!.Value;
        string fullness = request.Fullness!;
        string? notes = CleanNotes(request.Notes);
        bool isRacked = request.IsRacked.Value;

        bool reRack = !old.IsRacked && isRacked;
        bool unrack = old.IsRacked && !isRacked;

        // reset only counts on re-rack
        DateOnly oldSince = DateOnly.FromDateTime(old.RackedSince);
        DateOnly newSince = reRack && request.ResetRackedSince.Value ? DateOnly.FromDateTime(StoreClock.Today()) : oldSince;

        // details per entry (D-196, D-197)
        var rackedChanges = new List<Change>();
        var editedChanges = new List<Change>();

        if (request.CategoryID != old.CategoryId)
            editedChanges.Add(new("category", old.CatName, categoryName));

        // location goes in RACKED on re-rack, else EDITED
        if (request.BuildingID != old.BuildingId || letter != old.ShelfLetter || level != old.ShelfLevel) {
            var move = new Change("location", Location(old.BuildingName, old.ShelfLetter, old.ShelfLevel),
                Location(buildingName, letter, level));
            (reRack ? rackedChanges : editedChanges).Add(move);
        }

        if (fullness != old.Fullness)
            editedChanges.Add(new("fullness", old.Fullness, fullness));

        if (notes != old.Notes)
            editedChanges.Add(new("notes", old.Notes, notes));

        // same-day reset = no change
        if (newSince != oldSince)
            rackedChanges.Add(new("rackedSince", oldSince.ToString("yyyy-MM-dd"), newSince.ToString("yyyy-MM-dd")));

        // nothing changed: no write, lastUpdatedAt kept
        if (!reRack && !unrack && editedChanges.Count == 0) {
            await transaction.RollbackAsync(ct);
            return Ok((await LoadBoxAsync(connection, id, ct))!);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE BOX
            SET category_id = @catId, building_id = @buildId, shelf_letter = @letter, shelf_level = @level,
                fullness = @fullness, notes = @notes, is_racked = @isRacked, racked_since = @since,
                last_updated_at = UTC_TIMESTAMP(), last_updated_by = @employeeId
            WHERE id = @id
            """,
            new { id, catId = request.CategoryID, buildId = request.BuildingID, letter, level, fullness, notes,
                isRacked, since = newSince.ToDateTime(TimeOnly.MinValue), employeeId },
            transaction, cancellationToken: ct
        ));

        bool isElevated = await IsElevatedAsync(connection, transaction, deviceId, ct);

        AuditEntry Entry(AuditAction action, List<Change>? changes) => new() {
            EmployeeId = employeeId,
            IsElevated = isElevated,
            DeviceId = deviceId,
            ActionType = action,
            TargetType = AuditTarget.BOX,
            TargetId = id,
            TargetLabel = $"#{id}",
            // empty list = null
            Details = changes is { Count: > 0 } ? JsonSerializer.Serialize(new { changes }, detailsJson) : null,
        };

        // rack entry first, then EDITED
        if (unrack) await AuditLog.WriteAsync(connection, Entry(AuditAction.UNRACKED, null), transaction, ct);
        if (reRack) await AuditLog.WriteAsync(connection, Entry(AuditAction.RACKED, rackedChanges), transaction, ct);
        if (editedChanges.Count > 0)
            await AuditLog.WriteAsync(connection, Entry(AuditAction.EDITED, editedChanges), transaction, ct);

        await transaction.CommitAsync(ct);

        return Ok((await LoadBoxAsync(connection, id, ct))!);
    }

    // DELETE /api/boxes/{id}. soft delete, any employee
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteBox(int id, CancellationToken ct) {
        int employeeId = User.GetEmployeeId()!.Value;
        int deviceId = User.GetDeviceId()!.Value;
        await using var connection = await db.OpenConnectionAsync(ct);

        await using var transaction = await connection.BeginTransactionAsync(ct);

        // lock row. null = no box
        bool? deleted = await connection.ExecuteScalarAsync<bool?>(new CommandDefinition(
            "SELECT deleted_at IS NOT NULL FROM BOX WHERE id = @id FOR UPDATE",
            new { id }, transaction, cancellationToken: ct
        ));

        if (deleted == null) throw ApiError.NotFound("This box doesn't exist.");
        if (deleted.Value) throw ApiError.NotFound("This box was deleted.");

        // also counts as an update (D-176). same timestamp for both
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE BOX
            SET deleted_at = UTC_TIMESTAMP(), deleted_by = @employeeId,
                last_updated_at = UTC_TIMESTAMP(), last_updated_by = @employeeId
            WHERE id = @id
            """,
            new { id, employeeId }, transaction, cancellationToken: ct
        ));

        // no details for DELETED
        await AuditLog.WriteAsync(connection, new AuditEntry {
            EmployeeId = employeeId,
            IsElevated = await IsElevatedAsync(connection, transaction, deviceId, ct),
            DeviceId = deviceId,
            ActionType = AuditAction.DELETED,
            TargetType = AuditTarget.BOX,
            TargetId = id,
            TargetLabel = $"#{id}",
        }, transaction, ct);

        await transaction.CommitAsync(ct);

        return NoContent();
    }

    // Helpers

    // one audit change: {"field", "from", "to"}
    private record Change(string Field, object? From, object? To);

    // camelCase keys
    private static readonly JsonSerializerOptions detailsJson = new(JsonSerializerDefaults.Web);

    // shared POST/PUT rules
    private static void CheckFields(BoxFields request) {
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
    }

    // trimmed, blank = null
    private static string? CleanNotes(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    // as the app shows it
    private static string Location(string building, string letter, int level) =>
        $"{building} · Shelf {letter}, Level {level}";

    // both null, or same instant. DB holds UTC
    private static bool SameTime(DateTime? sent, DateTime? stored) {
        if (sent == null || stored == null) return sent == stored;
        var utc = sent.Value.Kind == DateTimeKind.Local ? sent.Value.ToUniversalTime() : sent.Value;
        return utc == stored.Value;
    }

    // 400 if gone
    private static async Task<string> CategoryNameAsync(DbConnection connection, DbTransaction transaction,
            int id, CancellationToken ct) {
        var name = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT name FROM CATEGORY WHERE id = @id", new { id }, transaction, cancellationToken: ct));
        return name ?? throw ApiError.BadRequest("That category no longer exists. Choose another.");
    }

    // 400 if gone
    private static async Task<string> BuildingNameAsync(DbConnection connection, DbTransaction transaction,
            int id, CancellationToken ct) {
        var name = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT name FROM BUILDING WHERE id = @id", new { id }, transaction, cancellationToken: ct));
        return name ?? throw ApiError.BadRequest("That building no longer exists. Choose another.");
    }

    // Master mode on this tablet
    private static Task<bool> IsElevatedAsync(DbConnection connection, DbTransaction transaction,
            int deviceId, CancellationToken ct) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT is_elevated FROM DEVICE WHERE id = @deviceId", new { deviceId }, transaction, cancellationToken: ct));

    // full box shape. null = no box. ! where it was just written
    private static async Task<Box?> LoadBoxAsync(DbConnection connection, int id, CancellationToken ct) {
        var row = await connection.QuerySingleOrDefaultAsync<BoxSummary>(new CommandDefinition(
            boxStructureQuery + " WHERE b.id = @id", new { id, today = StoreClock.Today() }, cancellationToken: ct));
        return row == null ? null : Box.FromRow(row);
    }
}
