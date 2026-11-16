using System.Security.Claims;
using System.Text.Json;
using Imp.Api.Controllers;
using Imp.Api.Data;
using Imp.Api.Errors;
using Imp.Api.Models;
using Imp.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Imp.Api.Tests;

// Tests for POST and DELETE /api/master-mode (docs/api/auth.md). They call the controller directly with a
// session token's claims, against the Docker database. The token checks themselves (signature, expiry, tablet
// removed, employee deactivated) belong to TokenEvents and are not repeated here.
//
// The Master email and password come from the sample data (db/sample_data.sql).
// If the database is not running these are skipped, like the other database tests.
public class MasterModeControllerTests {
    private const string SampleEmail = "master@example.com";
    private const string SamplePassword = "master123";

    // The exact sentences from auth.md.
    private const string WrongCredentials = "Incorrect email or password. Try again.";
    private const string SignedOut = "You've been signed out. Sign in again.";

    // Setup

    // A controller that sees the request as coming from this employee, signed in on this tablet.
    private static MasterModeController ControllerFor(int employeeId, int deviceId) {
        var identity = new ClaimsIdentity(new[] {
            new Claim(TokenClaims.Kind, TokenClaims.SessionKind),
            new Claim(TokenClaims.EmployeeId, employeeId.ToString()),
            new Claim(TokenClaims.DeviceId, deviceId.ToString()),
        }, "test");
        return new MasterModeController(new MySqlConnectionFactory(TestDatabase.ConnectionString), new BcryptPasswordHasher()) {
            ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    // Builds the request the way the app sends it, so this works whether MasterCredentialsRequest is a class or a record.
    private static MasterCredentialsRequest Credentials(string? email, string? password) =>
        JsonSerializer.Deserialize<MasterCredentialsRequest>(
            JsonSerializer.Serialize(new { email, password }), JsonSerializerOptions.Web)!;

    private static async Task RequireSampleMasterAsync(TestDatabase db) {
        var found = await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM MASTER_ACCOUNT WHERE email = @email", new { email = SampleEmail });
        Assert.True(found == 1,
            $"These tests sign in with the sample Master account ({SampleEmail} / {SamplePassword}). " +
            "Load db/sample_data.sql first (cd db && docker compose down -v && docker compose up -d).");
    }

    // Puts an employee on a tablet, with Master mode on or off.
    private static Task SignInAsync(TestDatabase db, int deviceId, int? employeeId, bool masterMode = false) =>
        db.ExecuteAsync(
            "UPDATE DEVICE SET active_employee_id = @employeeId, is_elevated = @masterMode WHERE id = @deviceId",
            new { employeeId, masterMode, deviceId });

    private static Task<bool> IsMasterModeOnAsync(TestDatabase db, int deviceId) =>
        db.ScalarAsync<bool>("SELECT is_elevated FROM DEVICE WHERE id = @deviceId", new { deviceId });

    private static Task<int?> ActiveEmployeeAsync(TestDatabase db, int deviceId) =>
        db.ScalarAsync<int?>("SELECT active_employee_id FROM DEVICE WHERE id = @deviceId", new { deviceId });

    // POST /api/master-mode

    // The right email and password turn Master mode on for this tablet and return 204 with no body.
    [DatabaseFact]
    public async Task TurnOn_RightCredentials_TurnsMasterModeOnAndReturns204() {
        await using var db = await TestDatabase.CreateAsync();
        await RequireSampleMasterAsync(db);
        await SignInAsync(db, db.DeviceId, db.EmployeeId);

        var result = await ControllerFor(db.EmployeeId, db.DeviceId)
            .TurnOn(Credentials(SampleEmail, SamplePassword), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.True(await IsMasterModeOnAsync(db, db.DeviceId));
    }

    // The employee stays signed in. Only Master mode changes.
    [DatabaseFact]
    public async Task TurnOn_KeepsTheEmployeeSignedIn() {
        await using var db = await TestDatabase.CreateAsync();
        await RequireSampleMasterAsync(db);
        await SignInAsync(db, db.DeviceId, db.EmployeeId);

        await ControllerFor(db.EmployeeId, db.DeviceId)
            .TurnOn(Credentials(SampleEmail, SamplePassword), CancellationToken.None);

        Assert.Equal(db.EmployeeId, await ActiveEmployeeAsync(db, db.DeviceId));
    }

    // Turning it on when it is already on is fine: 204 again, still on.
    [DatabaseFact]
    public async Task TurnOn_WhenAlreadyOn_Returns204AndStaysOn() {
        await using var db = await TestDatabase.CreateAsync();
        await RequireSampleMasterAsync(db);
        await SignInAsync(db, db.DeviceId, db.EmployeeId, masterMode: true);

        var result = await ControllerFor(db.EmployeeId, db.DeviceId)
            .TurnOn(Credentials(SampleEmail, SamplePassword), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.True(await IsMasterModeOnAsync(db, db.DeviceId));
    }

    // A wrong password is a 401 with auth.md's sentence and no code, and Master mode stays off.
    [DatabaseFact]
    public async Task TurnOn_WrongPassword_Returns401AndMasterModeStaysOff() {
        await using var db = await TestDatabase.CreateAsync();
        await RequireSampleMasterAsync(db);
        await SignInAsync(db, db.DeviceId, db.EmployeeId);

        var error = await Assert.ThrowsAsync<ApiError>(() => ControllerFor(db.EmployeeId, db.DeviceId)
            .TurnOn(Credentials(SampleEmail, "not-the-password"), CancellationToken.None));

        Assert.Equal(401, error.StatusCode);
        Assert.Equal(WrongCredentials, error.Message);
        Assert.Null(error.Code);
        Assert.False(await IsMasterModeOnAsync(db, db.DeviceId));
    }

    // A wrong email gets exactly the same answer as a wrong password, so the reply never says which one was wrong.
    [DatabaseFact]
    public async Task TurnOn_WrongEmail_GetsTheSameAnswerAsAWrongPassword() {
        await using var db = await TestDatabase.CreateAsync();
        await RequireSampleMasterAsync(db);
        await SignInAsync(db, db.DeviceId, db.EmployeeId);

        var error = await Assert.ThrowsAsync<ApiError>(() => ControllerFor(db.EmployeeId, db.DeviceId)
            .TurnOn(Credentials("someone.else@example.com", SamplePassword), CancellationToken.None));

        Assert.Equal(401, error.StatusCode);
        Assert.Equal(WrongCredentials, error.Message);
        Assert.Null(error.Code);
        Assert.False(await IsMasterModeOnAsync(db, db.DeviceId));
    }

    // A blank or missing email or password is also "wrong email or password". auth.md lists no 400 for this
    // endpoint, and POST /api/devices/renew treats these the same way.
    [DatabaseTheory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    [InlineData(SampleEmail, null)]
    [InlineData(SampleEmail, "")]
    [InlineData(SampleEmail, " ")]
    [InlineData(null, SamplePassword)]
    [InlineData("", SamplePassword)]
    [InlineData(" ", SamplePassword)]
    public async Task TurnOn_BlankOrMissingField_Returns401(string? email, string? password) {
        await using var db = await TestDatabase.CreateAsync();
        await RequireSampleMasterAsync(db);
        await SignInAsync(db, db.DeviceId, db.EmployeeId);

        var error = await Assert.ThrowsAsync<ApiError>(() => ControllerFor(db.EmployeeId, db.DeviceId)
            .TurnOn(Credentials(email, password), CancellationToken.None));

        Assert.Equal(401, error.StatusCode);
        Assert.Equal(WrongCredentials, error.Message);
        Assert.False(await IsMasterModeOnAsync(db, db.DeviceId));
    }

    // A failed attempt does not turn Master mode off if it was already on.
    [DatabaseFact]
    public async Task TurnOn_WrongPasswordWhileAlreadyOn_LeavesItOn() {
        await using var db = await TestDatabase.CreateAsync();
        await RequireSampleMasterAsync(db);
        await SignInAsync(db, db.DeviceId, db.EmployeeId, masterMode: true);

        await Assert.ThrowsAsync<ApiError>(() => ControllerFor(db.EmployeeId, db.DeviceId)
            .TurnOn(Credentials(SampleEmail, "not-the-password"), CancellationToken.None));

        Assert.True(await IsMasterModeOnAsync(db, db.DeviceId));
    }

    // Only the tablet in the token changes. Another tablet in the same store stays as it was.
    [DatabaseFact]
    public async Task TurnOn_DoesNotAffectOtherTablets() {
        await using var db = await TestDatabase.CreateAsync();
        await RequireSampleMasterAsync(db);
        var otherEmployee = await db.CreateEmployeeAsync();
        var otherDevice = await db.CreateDeviceAsync();
        await SignInAsync(db, db.DeviceId, db.EmployeeId);
        await SignInAsync(db, otherDevice, otherEmployee);

        await ControllerFor(db.EmployeeId, db.DeviceId)
            .TurnOn(Credentials(SampleEmail, SamplePassword), CancellationToken.None);

        Assert.True(await IsMasterModeOnAsync(db, db.DeviceId));
        Assert.False(await IsMasterModeOnAsync(db, otherDevice));
    }

    // If someone else signed in on the tablet while the password was being checked, the request ends with
    // sessionEnded and Master mode is NOT turned on for the new employee, who never typed the password.
    [DatabaseFact]
    public async Task TurnOn_TabletNowNamesSomeoneElse_ReturnsSessionEndedAndStaysOff() {
        await using var db = await TestDatabase.CreateAsync();
        await RequireSampleMasterAsync(db);
        var someoneElse = await db.CreateEmployeeAsync();
        await SignInAsync(db, db.DeviceId, someoneElse);

        var error = await Assert.ThrowsAsync<ApiError>(() => ControllerFor(db.EmployeeId, db.DeviceId)
            .TurnOn(Credentials(SampleEmail, SamplePassword), CancellationToken.None));

        Assert.Equal(401, error.StatusCode);
        Assert.Equal("sessionEnded", error.Code);
        Assert.Equal(SignedOut, error.Message);
        Assert.False(await IsMasterModeOnAsync(db, db.DeviceId));
        Assert.Equal(someoneElse, await ActiveEmployeeAsync(db, db.DeviceId));
    }

    // Same, when nobody is signed in on the tablet any more.
    [DatabaseFact]
    public async Task TurnOn_NobodySignedIn_ReturnsSessionEndedAndStaysOff() {
        await using var db = await TestDatabase.CreateAsync();
        await RequireSampleMasterAsync(db);
        await SignInAsync(db, db.DeviceId, employeeId: null);

        var error = await Assert.ThrowsAsync<ApiError>(() => ControllerFor(db.EmployeeId, db.DeviceId)
            .TurnOn(Credentials(SampleEmail, SamplePassword), CancellationToken.None));

        Assert.Equal("sessionEnded", error.Code);
        Assert.False(await IsMasterModeOnAsync(db, db.DeviceId));
    }

    // DELETE /api/master-mode

    // Turns Master mode off and returns 204 with no body. The employee stays signed in.
    [DatabaseFact]
    public async Task TurnOff_TurnsMasterModeOffKeepsEmployeeSignedInAndReturns204() {
        await using var db = await TestDatabase.CreateAsync();
        await SignInAsync(db, db.DeviceId, db.EmployeeId, masterMode: true);

        var result = await ControllerFor(db.EmployeeId, db.DeviceId).TurnOff(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.False(await IsMasterModeOnAsync(db, db.DeviceId));
        Assert.Equal(db.EmployeeId, await ActiveEmployeeAsync(db, db.DeviceId));
    }

    // Turning it off when it is already off is fine: 204, still off.
    [DatabaseFact]
    public async Task TurnOff_WhenAlreadyOff_Returns204AndStaysOff() {
        await using var db = await TestDatabase.CreateAsync();
        await SignInAsync(db, db.DeviceId, db.EmployeeId, masterMode: false);

        var result = await ControllerFor(db.EmployeeId, db.DeviceId).TurnOff(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.False(await IsMasterModeOnAsync(db, db.DeviceId));
    }

    // Only the tablet in the token changes.
    [DatabaseFact]
    public async Task TurnOff_DoesNotAffectOtherTablets() {
        await using var db = await TestDatabase.CreateAsync();
        var otherEmployee = await db.CreateEmployeeAsync();
        var otherDevice = await db.CreateDeviceAsync();
        await SignInAsync(db, db.DeviceId, db.EmployeeId, masterMode: true);
        await SignInAsync(db, otherDevice, otherEmployee, masterMode: true);

        await ControllerFor(db.EmployeeId, db.DeviceId).TurnOff(CancellationToken.None);

        Assert.False(await IsMasterModeOnAsync(db, db.DeviceId));
        Assert.True(await IsMasterModeOnAsync(db, otherDevice));
    }

    // A late request from an employee who has already been replaced on the tablet must not turn off Master
    // mode for the employee who is signed in now.
    [DatabaseFact]
    public async Task TurnOff_TabletNowNamesSomeoneElse_LeavesTheirMasterModeOn() {
        await using var db = await TestDatabase.CreateAsync();
        var someoneElse = await db.CreateEmployeeAsync();
        await SignInAsync(db, db.DeviceId, someoneElse, masterMode: true);

        await ControllerFor(db.EmployeeId, db.DeviceId).TurnOff(CancellationToken.None);

        Assert.True(await IsMasterModeOnAsync(db, db.DeviceId));
        Assert.Equal(someoneElse, await ActiveEmployeeAsync(db, db.DeviceId));
    }

    // Both endpoints

    // Turning Master mode on and off is not logged (auth.md: none of these endpoints write audit entries).
    [DatabaseFact]
    public async Task TurningMasterModeOnAndOff_WritesNoAuditEntries() {
        await using var db = await TestDatabase.CreateAsync();
        await RequireSampleMasterAsync(db);
        await SignInAsync(db, db.DeviceId, db.EmployeeId);
        var controller = ControllerFor(db.EmployeeId, db.DeviceId);

        await controller.TurnOn(Credentials(SampleEmail, SamplePassword), CancellationToken.None);
        await controller.TurnOff(CancellationToken.None);

        Assert.Empty(await db.RowsAsync());
    }

    // The whole round trip: off, on, off.
    [DatabaseFact]
    public async Task MasterMode_OnThenOff_EndsOffAgain() {
        await using var db = await TestDatabase.CreateAsync();
        await RequireSampleMasterAsync(db);
        await SignInAsync(db, db.DeviceId, db.EmployeeId);
        var controller = ControllerFor(db.EmployeeId, db.DeviceId);
        Assert.False(await IsMasterModeOnAsync(db, db.DeviceId));

        await controller.TurnOn(Credentials(SampleEmail, SamplePassword), CancellationToken.None);
        Assert.True(await IsMasterModeOnAsync(db, db.DeviceId));

        await controller.TurnOff(CancellationToken.None);
        Assert.False(await IsMasterModeOnAsync(db, db.DeviceId));
    }
}
