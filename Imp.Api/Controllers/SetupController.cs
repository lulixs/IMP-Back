using Dapper;
using Imp.Api.Data;
using Imp.Api.Errors;
using Imp.Api.Models;
using Imp.Api.Security;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace Imp.Api.Controllers;

// First-time setup, from docs/api/auth.md. Neither endpoint takes a token.
[ApiController]
[Route("api/setup")]
public class SetupController : ControllerBase {
    private const string AlreadySetUp = "This store is already set up. Set up this tablet instead.";

    private readonly IDbConnectionFactory db;
    private readonly IPasswordHasher hasher;
    private readonly TokenService tokens;

    public SetupController(IDbConnectionFactory db, IPasswordHasher hasher, TokenService tokens) {
        this.db = db;
        this.hasher = hasher;
        this.tokens = tokens;
    }

    // GET /api/setup/status. Tells a tablet with no device token whether to open first-time setup or tablet setup.
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct) {
        await using var connection = await db.OpenConnectionAsync(ct);
        var isSetUp = await MasterAccount.ExistsAsync(connection, ct);
        return Ok(new { isSetUp });
    }

    // POST /api/setup. Creates the Master account, this tablet, and the first employee, and signs that employee in.
    [HttpPost]
    public async Task<IActionResult> SetUp(SetupRequest request, CancellationToken ct) {
        var masterEmail = request.MasterEmail?.Trim();
        var tabletName = request.TabletName?.Trim();
        var employeeName = request.EmployeeName?.Trim();

        // auth.md asks for a sentence naming the field. It gives "Enter a valid email." and leaves the rest to us.
        if (!InputRules.IsValidEmail(masterEmail)) {
            throw ApiError.BadRequest("Enter a valid email.");
        }
        if (!InputRules.IsValidPassword(request.MasterPassword)) {
            throw ApiError.BadRequest("Enter a password of at least 8 characters.");
        }
        if (!InputRules.IsValidName(tabletName)) {
            throw ApiError.BadRequest("Enter a tablet name of 1 to 100 characters.");
        }
        if (!InputRules.IsValidName(employeeName)) {
            throw ApiError.BadRequest("Enter an employee name of 1 to 100 characters.");
        }
        if (!InputRules.IsValidPin(request.EmployeePin)) {
            throw ApiError.BadRequest("Enter a 6-digit PIN.");
        }

        await using var connection = await db.OpenConnectionAsync(ct);

        // Answer a second setup quickly, before spending time on bcrypt.
        if (await MasterAccount.ExistsAsync(connection, ct)) {
            throw ApiError.Conflict(AlreadySetUp);
        }

        // bcrypt at cost 12 (D-162). Only the hashes go to the database. The plain values are never stored or logged.
        var passwordHash = hasher.Hash(request.MasterPassword!);
        var pinHash = hasher.Hash(request.EmployeePin!);

        // All of it is saved together or not at all. Disposing the transaction without committing rolls it back.
        await using var transaction = await connection.BeginTransactionAsync(ct);

        try {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO MASTER_ACCOUNT (id, email, password_hash) VALUES (1, @masterEmail, @passwordHash)",
                new { masterEmail, passwordHash }, transaction, cancellationToken: ct));
        } catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) {
            // Two tablets ran setup at the same moment, and the other one got there first.
            throw ApiError.Conflict(AlreadySetUp);
        }

        // A new tablet starts with Master mode off.
        var deviceId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "INSERT INTO DEVICE (device_name) VALUES (@tabletName); SELECT LAST_INSERT_ID();",
            new { tabletName }, transaction, cancellationToken: ct));

        var employeeId = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO EMPLOYEE_ACCOUNT (name, pin_hash, last_sign_in_at) VALUES (@employeeName, @pinHash, UTC_TIMESTAMP());
            SELECT LAST_INSERT_ID();
            """, new { employeeName, pinHash }, transaction, cancellationToken: ct));

        // The first employee is signed in on this tablet.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE DEVICE SET active_employee_id = @employeeId WHERE id = @deviceId",
            new { employeeId, deviceId }, transaction, cancellationToken: ct));

        // EMPLOYEE ADDED, by that same employee, on this tablet (D-118). It is marked as done in Master mode,
        // like the setup entry in sample_data.sql, because setup is done with the Master email and password.
        await AuditLog.WriteAsync(connection, new AuditEntry {
            EmployeeId = employeeId,
            IsElevated = true,
            DeviceId = deviceId,
            ActionType = AuditAction.ADDED,
            TargetType = AuditTarget.EMPLOYEE,
            TargetId = employeeId,
            TargetLabel = employeeName!,
        }, transaction, ct);

        await transaction.CommitAsync(ct);

        return StatusCode(StatusCodes.Status201Created, new {
            deviceToken = tokens.CreateDeviceToken(deviceId),
            sessionToken = tokens.CreateSessionToken(employeeId, deviceId),
            employee = new { id = employeeId, name = employeeName },
        });
    }
}
