using System.Reflection;
using System.Security.Claims;
using Imp.Api.Data;
using Imp.Api.Errors;
using Imp.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit;

namespace Imp.Api.Tests;

// Tests for [RequireMasterMode]. They run the attribute's filter directly with a made-up request, the way
// ASP.NET Core would just before the endpoint, and check whether the endpoint was reached.
// Tests that read DEVICE.is_elevated need the Docker database and are skipped without it.
// The session token itself (signature, expiry, tablet removed, ...) is TokenEvents' job and is not tested here.
public class RequireMasterModeAttributeTests {
    // The sentence from the shared error rules (docs/api/README.md, status 403).
    private const string TurnOnMasterModeFirst = "Turn on Master mode first.";
    private const string SignedOut = "You've been signed out. Sign in again.";

    // Setup

    // What happened when the filter ran: did the endpoint run, and was an exception thrown?
    private sealed class Outcome {
        public bool EndpointReached { get; set; }
        public Exception? Error { get; set; }
    }

    // A session token's claims. deviceId is a string so tests can send values that are not numbers.
    private static ClaimsPrincipal Session(string? deviceId, int employeeId = 1) {
        var claims = new List<Claim> {
            new(TokenClaims.Kind, TokenClaims.SessionKind),
            new(TokenClaims.EmployeeId, employeeId.ToString()),
        };
        if (deviceId is not null) claims.Add(new Claim(TokenClaims.DeviceId, deviceId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal Session(int deviceId) => Session(deviceId.ToString());

    private static IServiceProvider RealDatabase() => ServiceProviderFor(TestDatabase.ConnectionString);

    private static IServiceProvider ServiceProviderFor(string connectionString) =>
        new ServiceCollection()
            .AddSingleton<IDbConnectionFactory>(new MySqlConnectionFactory(connectionString))
            .BuildServiceProvider();

    // Runs the filter for one request.
    private static async Task<Outcome> RunAsync(IServiceProvider services, ClaimsPrincipal user,
        CancellationToken requestAborted = default) {
        var outcome = new Outcome();
        var http = new DefaultHttpContext { User = user, RequestServices = services, RequestAborted = requestAborted };
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(
            actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());

        // Stands in for the endpoint. If the filter lets the request through, it calls this.
        Task<ActionExecutedContext> Endpoint() {
            outcome.EndpointReached = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        }

        try {
            await new RequireMasterModeAttribute().OnActionExecutionAsync(executing, Endpoint);
        } catch (Exception ex) {
            outcome.Error = ex;
        }
        return outcome;
    }

    private static Task SetMasterModeAsync(TestDatabase db, int deviceId, bool on) =>
        db.ExecuteAsync("UPDATE DEVICE SET is_elevated = @on WHERE id = @deviceId", new { on, deviceId });

    // Master mode on

    // Master mode is on for the tablet in the token, so the request reaches the endpoint.
    [DatabaseFact]
    public async Task MasterModeOn_LetsTheRequestThrough() {
        await using var db = await TestDatabase.CreateAsync();
        await SetMasterModeAsync(db, db.DeviceId, on: true);

        var outcome = await RunAsync(RealDatabase(), Session(db.DeviceId));

        Assert.Null(outcome.Error);
        Assert.True(outcome.EndpointReached);
    }

    // Master mode off

    // Master mode is off: 403 with the sentence from the contract, no code, and the endpoint never runs.
    [DatabaseFact]
    public async Task MasterModeOff_Returns403AndTheEndpointDoesNotRun() {
        await using var db = await TestDatabase.CreateAsync();
        await SetMasterModeAsync(db, db.DeviceId, on: false);

        var outcome = await RunAsync(RealDatabase(), Session(db.DeviceId));

        var error = Assert.IsType<ApiError>(outcome.Error);
        Assert.Equal(403, error.StatusCode);
        Assert.Equal(TurnOnMasterModeFirst, error.Message);
        Assert.Null(error.Code);
        Assert.False(outcome.EndpointReached);
    }

    // A brand new tablet starts with Master mode off, so it is refused too.
    [DatabaseFact]
    public async Task NewTablet_IsRefused() {
        await using var db = await TestDatabase.CreateAsync();

        var outcome = await RunAsync(RealDatabase(), Session(db.DeviceId));

        Assert.Equal(403, Assert.IsType<ApiError>(outcome.Error).StatusCode);
        Assert.False(outcome.EndpointReached);
    }

    // Which tablet

    // Only the tablet in the token counts. Master mode on another tablet does not help this one,
    // and the other way round.
    [DatabaseFact]
    public async Task OnlyTheTabletInTheTokenCounts() {
        await using var db = await TestDatabase.CreateAsync();
        var otherDevice = await db.CreateDeviceAsync();
        await SetMasterModeAsync(db, db.DeviceId, on: false);
        await SetMasterModeAsync(db, otherDevice, on: true);
        var services = RealDatabase();

        var offTablet = await RunAsync(services, Session(db.DeviceId));
        var onTablet = await RunAsync(services, Session(otherDevice));

        Assert.Equal(403, Assert.IsType<ApiError>(offTablet.Error).StatusCode);
        Assert.False(offTablet.EndpointReached);
        Assert.Null(onTablet.Error);
        Assert.True(onTablet.EndpointReached);
    }

    // A tablet id that is not in the database is refused, not let through.
    [DatabaseTheory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task UnknownTablet_IsRefused(int unknownDeviceId) {
        await using var db = await TestDatabase.CreateAsync();

        var outcome = await RunAsync(RealDatabase(), Session(unknownDeviceId));

        Assert.Equal(403, Assert.IsType<ApiError>(outcome.Error).StatusCode);
        Assert.False(outcome.EndpointReached);
    }

    // Read fresh every time

    // Master mode is not remembered between requests. It can be turned off (or on) at any moment,
    // for example by DELETE /api/master-mode or by the employee signing out, and the next request sees that.
    [DatabaseFact]
    public async Task MasterModeIsReadOnEveryRequest() {
        await using var db = await TestDatabase.CreateAsync();
        var services = RealDatabase();
        var user = Session(db.DeviceId);

        await SetMasterModeAsync(db, db.DeviceId, on: true);
        var whileOn = await RunAsync(services, user);
        await SetMasterModeAsync(db, db.DeviceId, on: false);
        var afterOff = await RunAsync(services, user);
        await SetMasterModeAsync(db, db.DeviceId, on: true);
        var onAgain = await RunAsync(services, user);

        Assert.True(whileOn.EndpointReached);
        Assert.False(afterOff.EndpointReached);
        Assert.Equal(403, Assert.IsType<ApiError>(afterOff.Error).StatusCode);
        Assert.True(onAgain.EndpointReached);
    }

    // The filter only reads. It never changes Master mode or anything else on the tablet.
    [DatabaseFact]
    public async Task TheFilterDoesNotChangeTheTablet() {
        await using var db = await TestDatabase.CreateAsync();
        await SetMasterModeAsync(db, db.DeviceId, on: true);
        var services = RealDatabase();

        await RunAsync(services, Session(db.DeviceId));

        Assert.True(await db.ScalarAsync<bool>("SELECT is_elevated FROM DEVICE WHERE id = @id", new { id = db.DeviceId }));
        Assert.Null(await db.ScalarAsync<int?>("SELECT active_employee_id FROM DEVICE WHERE id = @id", new { id = db.DeviceId }));
    }

    // No usable tablet in the token (needs no database)

    // If no tablet id reaches the filter, [Authorize] is missing or wrong on the endpoint. The request is refused
    // with sessionEnded, and the database is never asked (these services have no database at all).
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public async Task NoUsableTabletInTheToken_Returns401SessionEnded(string? deviceId) {
        var noServices = new ServiceCollection().BuildServiceProvider();

        var outcome = await RunAsync(noServices, Session(deviceId));

        var error = Assert.IsType<ApiError>(outcome.Error);
        Assert.Equal(401, error.StatusCode);
        Assert.Equal("sessionEnded", error.Code);
        Assert.Equal(SignedOut, error.Message);
        Assert.False(outcome.EndpointReached);
    }

    // No token at all (an anonymous request) is refused the same way.
    [Fact]
    public async Task NoToken_Returns401SessionEnded() {
        var noServices = new ServiceCollection().BuildServiceProvider();

        var outcome = await RunAsync(noServices, new ClaimsPrincipal(new ClaimsIdentity()));

        var error = Assert.IsType<ApiError>(outcome.Error);
        Assert.Equal(401, error.StatusCode);
        Assert.Equal("sessionEnded", error.Code);
        Assert.False(outcome.EndpointReached);
    }

    // When the check cannot be made (needs no database running)

    // If the database cannot be reached, the request is NOT let through, and it is not a 403 either.
    // It is the ordinary exception the error handler in Program.cs turns into a 500, so a database problem
    // never looks like "Master mode is off". Port 1 has nothing listening, so this fails straight away.
    [Fact]
    public async Task DatabaseUnreachable_DoesNotLetTheRequestThroughAndIsNotA403() {
        var unreachable = ServiceProviderFor("Server=localhost;Port=1;Database=IMP;User=imp;Password=imp");

        var outcome = await RunAsync(unreachable, Session(1));

        Assert.IsAssignableFrom<MySqlException>(outcome.Error);
        Assert.False(outcome.EndpointReached);
    }

    // A request the caller has already given up on stops without reaching the endpoint.
    // (Accepts the MySqlException MySqlConnector uses when the cancel reaches the server.)
    [DatabaseFact]
    public async Task CancelledRequest_DoesNotReachTheEndpoint() {
        await using var db = await TestDatabase.CreateAsync();
        await SetMasterModeAsync(db, db.DeviceId, on: true);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var outcome = await RunAsync(RealDatabase(), Session(db.DeviceId), cancelled.Token);

        Assert.True(outcome.Error is OperationCanceledException or MySqlException,
            $"Expected a cancellation error but got: {outcome.Error?.GetType().Name ?? "no error"}");
        Assert.False(outcome.EndpointReached);
    }

    // How the attribute is declared (needs no database)

    // It can go on a single endpoint or a whole controller, and ASP.NET Core runs it as a filter.
    [Fact]
    public void Attribute_CanGoOnAMethodOrAClass_AndIsAnActionFilter() {
        var usage = typeof(RequireMasterModeAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Class | AttributeTargets.Method, usage!.ValidOn);
        Assert.True(typeof(IAsyncActionFilter).IsAssignableFrom(typeof(RequireMasterModeAttribute)));
    }

    // The sentence in the code is the one in the contract.
    [Fact]
    public void Message_IsTheSentenceFromTheContract() {
        Assert.Equal(TurnOnMasterModeFirst, RequireMasterModeAttribute.Message);
    }
}