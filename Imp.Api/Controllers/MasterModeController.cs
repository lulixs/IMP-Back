using Dapper;
using Imp.Api.Data;
using Imp.Api.Errors;
using Imp.Api.Models;
using Imp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Imp.Api.Controllers;

// Master mode on and off for one tablet
[ApiController]
[Route("api/master-mode")]
[Authorize(Policy = AuthPolicies.Session)]
public class MasterModeController : ControllerBase {
    private readonly IDbConnectionFactory db;
    private readonly IPasswordHasher hasher;

    public MasterModeController(IDbConnectionFactory db, IPasswordHasher hasher) {
        this.db = db;
        this.hasher = hasher;
    }

    // POST /api/master-mode
    [HttpPost]
    public async Task<IActionResult> TurnOn(MasterCredentialsRequest request, CancellationToken ct) {
        var deviceId = User.GetDeviceId()!.Value;
        var employeeId = User.GetEmployeeId()!.Value;

        await using var connection = await db.OpenConnectionAsync(ct);

        if (!await MasterAccount.CheckCredentialsAsync(connection, hasher, request.Email, request.Password, ct)) {
            throw ApiError.Unauthorized(MasterAccount.WrongCredentials);
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE DEVICE SET is_elevated = TRUE
            WHERE id = @deviceId AND active_employee_id = @employeeId
            """, new { deviceId, employeeId }, cancellationToken: ct));
        if (updated == 0) {
            throw ApiError.Unauthorized(AuthErrors.MessageFor(AuthErrors.SessionEnded), AuthErrors.SessionEnded);
        }

        return NoContent();
    }

    // DELETE /api/master-mode
    [HttpDelete]
    public async Task<IActionResult> TurnOff(CancellationToken ct) {
        var deviceId = User.GetDeviceId()!.Value;
        var employeeId = User.GetEmployeeId()!.Value;

        await using var connection = await db.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE DEVICE SET is_elevated = FALSE
            WHERE id = @deviceId AND active_employee_id = @employeeId
            """, new { deviceId, employeeId }, cancellationToken: ct));

        return NoContent();
    }
}
