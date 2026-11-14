using Dapper;
using Imp.Api.Data;
using Imp.Api.Errors;
using Imp.Api.Models;
using Imp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Imp.Api.Controllers;

// Employee sign-in and sign-out on a tablet, from docs/api/auth.md. None of this writes audit entries (D-142).
[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase {
    private readonly IDbConnectionFactory db;
    private readonly IPasswordHasher hasher;
    private readonly TokenService tokens;

    public SessionsController(IDbConnectionFactory db, IPasswordHasher hasher, TokenService tokens) {
        this.db = db;
        this.hasher = hasher;
        this.tokens = tokens;
    }

    // POST /api/sessions. Signs an employee in on this tablet with their PIN and returns a session token.
    [HttpPost]
    [Authorize(Policy = AuthPolicies.Device)]
    public async Task<IActionResult> CreateSession(SignInRequest request, CancellationToken ct) {
        // auth.md gives one sentence for a PIN that isn't 6 digits, and for any missing field.
        if (request.EmployeeId is null || !InputRules.IsValidPin(request.Pin)) {
            throw ApiError.BadRequest("Enter your 6-digit PIN.");
        }

        // TokenEvents has already checked that this tablet exists and isn't removed.
        var deviceId = User.GetDeviceId()!.Value;
        var employeeId = request.EmployeeId.Value;

        await using var connection = await db.OpenConnectionAsync(ct);

        var employee = await connection.QuerySingleOrDefaultAsync<SignInEmployee>(new CommandDefinition(
            "SELECT id, name, pin_hash, is_activated FROM EMPLOYEE_ACCOUNT WHERE id = @employeeId",
            new { employeeId }, cancellationToken: ct));

        // Checked before the PIN, so a deactivated employee is told to choose again even with the right PIN.
        if (employee is null || !employee.IsActivated) {
            throw ApiError.Unauthorized("This account can't sign in. Choose your name again.");
        }
        if (!hasher.Verify(request.Pin!, employee.PinHash)) {
            throw ApiError.Unauthorized("Incorrect PIN. Try again.");
        }

        // Both changes are saved together. If another employee was signed in on this tablet, their session
        // ends here, because the tablet now names this employee. Other tablets are not affected (D-151).
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            // Master mode starts off with every sign-in.
            "UPDATE DEVICE SET active_employee_id = @employeeId, is_elevated = FALSE WHERE id = @deviceId",
            new { employeeId, deviceId }, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE EMPLOYEE_ACCOUNT SET last_sign_in_at = UTC_TIMESTAMP() WHERE id = @employeeId",
            new { employeeId }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);

        return Ok(new {
            sessionToken = tokens.CreateSessionToken(employeeId, deviceId),
            employee = new { id = employee.Id, name = employee.Name },
        });
    }

    // DELETE /api/sessions/current. Signs the employee out of this tablet, for Switch user and idle sign-out.
    // Master mode turns off too (D-117). Signing out again gets sessionEnded from TokenEvents, since the
    // tablet no longer names this employee.
    [HttpDelete("current")]
    [Authorize(Policy = AuthPolicies.Session)]
    public async Task<IActionResult> EndCurrentSession(CancellationToken ct) {
        var deviceId = User.GetDeviceId()!.Value;
        var employeeId = User.GetEmployeeId()!.Value;

        await using var connection = await db.OpenConnectionAsync(ct);
        // "AND active_employee_id = @employeeId" stops a late sign-out from signing out someone
        // who signed in on this tablet a moment ago.
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE DEVICE SET active_employee_id = NULL, is_elevated = FALSE
            WHERE id = @deviceId AND active_employee_id = @employeeId
            """, new { deviceId, employeeId }, cancellationToken: ct));

        return NoContent();
    }

    internal sealed class SignInEmployee {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string PinHash { get; set; } = "";
        public bool IsActivated { get; set; }
    }
}
