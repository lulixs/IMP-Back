using Dapper;
using Imp.Api.Data;
using Imp.Api.Errors;
using Imp.Api.Models;
using Imp.Api.Security;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace Imp.Api.Controllers;

// Setting up another tablet, and signing a tablet back in after its 90 days, from docs/api/auth.md.
[ApiController]
[Route("api/devices")]
public class DevicesController : ControllerBase {
    private const string NameTaken = "A tablet with this name already exists. Choose another name.";

    private readonly IDbConnectionFactory db;
    private readonly IPasswordHasher hasher;
    private readonly TokenService tokens;

    public DevicesController(IDbConnectionFactory db, IPasswordHasher hasher, TokenService tokens) {
        this.db = db;
        this.hasher = hasher;
        this.tokens = tokens;
    }

    // POST /api/devices. No token. Checks the Master email and password, then creates the tablet's DEVICE row.
    [HttpPost]
    public async Task<IActionResult> Create(NewDeviceRequest request, CancellationToken ct) {
        var tabletName = request.TabletName?.Trim();

        // auth.md gives one sentence for a blank or too long name, and for any missing field.
        if (!InputRules.IsValidName(tabletName) || request.Email is null || request.Password is null) {
            throw ApiError.BadRequest("Enter a tablet name.");
        }

        await using var connection = await db.OpenConnectionAsync(ct);

        if (!await MasterAccount.ExistsAsync(connection, ct)) {
            throw ApiError.Conflict("This store isn't set up yet.");
        }
        // Checked before the name, so someone without the password can't find out which tablet names exist.
        if (!await MasterAccount.CheckCredentialsAsync(connection, hasher, request.Email, request.Password, ct)) {
            throw ApiError.Unauthorized(MasterAccount.WrongCredentials);
        }

        var existing = await connection.QuerySingleOrDefaultAsync<ExistingDevice>(new CommandDefinition(
            "SELECT id, removed_at FROM DEVICE WHERE device_name = @tabletName",
            new { tabletName }, cancellationToken: ct));

        int deviceId;
        if (existing is null) {
            // A new tablet starts with no employee signed in and Master mode off.
            try {
                deviceId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "INSERT INTO DEVICE (device_name) VALUES (@tabletName); SELECT LAST_INSERT_ID();",
                    new { tabletName }, cancellationToken: ct));
            } catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) {
                // Another tablet took this name at the same moment.
                throw ApiError.Conflict(NameTaken);
            }
        } else if (existing.RemovedAt is null) {
            throw ApiError.Conflict(NameTaken);
        } else {
            // The name belongs to a removed tablet, so that row comes back and its id and audit history
            // carry on (D-166). It starts like a new tablet: no employee signed in and Master mode off.
            // "AND removed_at IS NOT NULL" means only one of two tablets setting up at once gets the row.
            var updated = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE DEVICE
                SET device_name = @tabletName, removed_at = NULL, active_employee_id = NULL,
                    is_elevated = FALSE, last_activity_at = UTC_TIMESTAMP()
                WHERE id = @id AND removed_at IS NOT NULL
                """, new { tabletName, id = existing.Id }, cancellationToken: ct));
            if (updated == 0) {
                throw ApiError.Conflict(NameTaken);
            }
            deviceId = existing.Id;
        }

        return StatusCode(StatusCodes.Status201Created, new {
            deviceToken = tokens.CreateDeviceToken(deviceId),
            device = new { id = deviceId, name = tabletName },
        });
    }

    // POST /api/devices/renew. Takes the device token whether or not it has expired, and checks only its
    // signature, so it has no [Authorize]. Then checks the Master email and password and sends a new device token.
    [HttpPost("renew")]
    public async Task<IActionResult> Renew(MasterCredentialsRequest request, CancellationToken ct) {
        var deviceId = await tokens.ReadDeviceIdIgnoringExpiryAsync(BearerToken());
        if (deviceId is null) {
            throw TokenError(AuthErrors.DeviceNotSetUp);
        }

        await using var connection = await db.OpenConnectionAsync(ct);

        var device = await connection.QuerySingleOrDefaultAsync<ExistingDevice>(new CommandDefinition(
            "SELECT id, removed_at FROM DEVICE WHERE id = @deviceId",
            new { deviceId }, cancellationToken: ct));
        if (device is null) {
            throw TokenError(AuthErrors.DeviceNotSetUp);
        }
        if (device.RemovedAt is not null) {
            throw TokenError(AuthErrors.DeviceRemoved);
        }

        if (!await MasterAccount.CheckCredentialsAsync(connection, hasher, request.Email, request.Password, ct)) {
            throw ApiError.Unauthorized(MasterAccount.WrongCredentials);
        }

        // Nothing else changes in the database. The tablet keeps its id, name, and signed-in employee.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE DEVICE SET last_activity_at = UTC_TIMESTAMP() WHERE id = @deviceId",
            new { deviceId }, cancellationToken: ct));

        return Ok(new { deviceToken = tokens.CreateDeviceToken(deviceId.Value) });
    }

    // The token from "Authorization: Bearer <token>", or null if there isn't one.
    private string? BearerToken() {
        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..].Trim() : null;
    }

    private static ApiError TokenError(string code) => ApiError.Unauthorized(AuthErrors.MessageFor(code), code);

    internal sealed class ExistingDevice {
        public int Id { get; set; }
        public DateTime? RemovedAt { get; set; }
    }
}
