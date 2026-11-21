using System.Security.Claims;
using Dapper;
using Imp.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Imp.Api.Security;

// JwtBearer runs these on every request that carries a token. JwtBearer itself has already checked the
// signature and exp before TokenValidated runs. This class adds the database checks from docs/api/auth.md,
// and writes the 401 reply in the shared error format when an endpoint's policy isn't met.
//
// If the database is down, the exception is not caught here. JwtBearer passes it on, and the error handler in
// Program.cs turns it into a 500, so a database problem never signs anyone out.
public class TokenEvents : JwtBearerEvents {
    // Where TokenValidated leaves the code for Challenge to send.
    private const string FailureCodeKey = "Imp.TokenFailureCode";

    private readonly IDbConnectionFactory db;

    public TokenEvents(IDbConnectionFactory db) {
        this.db = db;
    }

    // The token's signature and exp are good. Now check it against the endpoint and the database.
    public override async Task TokenValidated(TokenValidatedContext context) {
        var http = context.HttpContext;
        var requiredKind = AuthPolicies.RequiredKind(http.GetEndpoint());

        var code = await CheckAsync(context.Principal!, requiredKind, http.RequestAborted);
        if (code is not null) {
            http.Items[FailureCodeKey] = code;
            context.Fail($"Token refused with {code}.");
        }
    }

    // An endpoint with [Authorize] was called without a token that passed. Send the 401 the app expects.
    public override async Task Challenge(JwtBearerChallengeContext context) {
        // Stops JwtBearer from sending its own empty 401.
        context.HandleResponse();

        var http = context.HttpContext;
        var requiredKind = AuthPolicies.RequiredKind(http.GetEndpoint());
        var code = http.Items[FailureCodeKey] as string
            ?? CodeForFailedToken(requiredKind, context.AuthenticateFailure);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        await context.Response.WriteAsJsonAsync(new { error = AuthErrors.MessageFor(code), code });
    }

    // The code for a token that is missing, or that failed JwtBearer's own checks (signature or exp).
    // Only a device token gets deviceExpired. A session token that is expired is sessionEnded.
    public static string CodeForFailedToken(string? requiredKind, Exception? failure) {
        if (requiredKind == TokenClaims.DeviceKind && failure is SecurityTokenExpiredException) {
            return AuthErrors.DeviceExpired;
        }
        return AuthErrors.ForMissingToken(requiredKind);
    }

    // The code for a token of the wrong kind for this endpoint, or null if the kind is fine.
    // Endpoints with no policy take either kind.
    public static string? CodeForWrongKind(string? requiredKind, string? tokenKind) {
        if (tokenKind != TokenClaims.DeviceKind && tokenKind != TokenClaims.SessionKind) {
            return AuthErrors.ForMissingToken(requiredKind);
        }
        if (requiredKind is not null && tokenKind != requiredKind) {
            return AuthErrors.ForMissingToken(requiredKind);
        }
        return null;
    }

    // Returns the error code, or null if the token is good. On a good token, also sets
    // DEVICE.last_activity_at to now, since every request with a valid token does.
    private async Task<string?> CheckAsync(ClaimsPrincipal user, string? requiredKind, CancellationToken ct) {
        var tokenKind = user.FindFirst(TokenClaims.Kind)?.Value;
        var wrongKind = CodeForWrongKind(requiredKind, tokenKind);
        if (wrongKind is not null) return wrongKind;

        var isSession = tokenKind == TokenClaims.SessionKind;
        var notValid = isSession ? AuthErrors.SessionEnded : AuthErrors.DeviceNotSetUp;

        var deviceId = user.GetDeviceId();
        var employeeId = user.GetEmployeeId();
        if (deviceId is null || (isSession && employeeId is null)) return notValid;

        await using var connection = await db.OpenConnectionAsync(ct);
        var device = await connection.QuerySingleOrDefaultAsync<DeviceStatus>(new CommandDefinition("""
            SELECT d.removed_at, d.active_employee_id, e.is_activated AS employee_is_activated
            FROM DEVICE AS d
            LEFT JOIN EMPLOYEE_ACCOUNT AS e ON e.id = d.active_employee_id
            WHERE d.id = @deviceId
            """, new { deviceId }, cancellationToken: ct));

        // The order is the one in auth.md: the tablet exists and isn't removed, then (for a session)
        // the tablet still names this employee, then that employee is activated.
        if (device is null) return notValid;
        if (device.RemovedAt is not null) return AuthErrors.DeviceRemoved;
        if (isSession) {
            if (device.ActiveEmployeeId != employeeId) return AuthErrors.SessionEnded;
            if (device.EmployeeIsActivated != true) return AuthErrors.SessionEnded;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE DEVICE SET last_activity_at = UTC_TIMESTAMP() WHERE id = @deviceId",
            new { deviceId }, cancellationToken: ct));
        return null;
    }

    // The parts of a DEVICE row (and its signed-in employee) the checks need.
    internal sealed class DeviceStatus {
        public DateTime? RemovedAt { get; set; }
        public int? ActiveEmployeeId { get; set; }
        public bool? EmployeeIsActivated { get; set; }
    }
}
