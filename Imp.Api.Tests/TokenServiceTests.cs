using System.Security.Claims;
using Imp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Imp.Api.Tests;

// Tests for TokenService, the Session and Device policies, and the error codes TokenEvents picks.
// None of these need the database.
public class TokenServiceTests {
    private const string Secret = "test-only-secret-that-is-long-enough-for-hs256";
    private readonly TokenService _tokens = new(Secret);
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };

    // Checks a token the same way JwtBearer does, with the same settings.
    private async Task<TokenValidationResult> ValidateAsync(string token, TokenService? checker = null) {
        return await _handler.ValidateTokenAsync(token, (checker ?? _tokens).ValidationParameters);
    }

    // The same two policies Program.cs registers.
    private static IAuthorizationService Policies() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(AuthPolicies.AddTo);
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private async Task<ClaimsPrincipal> SignedInAs(string token) {
        var result = await ValidateAsync(token);
        Assert.True(result.IsValid);
        return new ClaimsPrincipal(result.ClaimsIdentity);
    }

    // What each token holds

    // A session token holds the employee and tablet it was made for, and says it is a session token.
    [Fact]
    public async Task SessionToken_HoldsTheIdsItWasMadeWith() {
        var token = _tokens.CreateSessionToken(employeeId: 7, deviceId: 3);

        var user = await SignedInAs(token);

        Assert.Equal(TokenClaims.SessionKind, user.FindFirst(TokenClaims.Kind)?.Value);
        Assert.Equal(7, user.GetEmployeeId());
        Assert.Equal(3, user.GetDeviceId());
    }

    // sub is a string in the token, as in auth.md's example ("sub": "1"), and deviceId is a number.
    [Fact]
    public void SessionToken_StoresSubAsStringAndDeviceIdAsNumber() {
        var jwt = new JsonWebToken(_tokens.CreateSessionToken(employeeId: 1, deviceId: 3));

        Assert.Equal("1", jwt.GetPayloadValue<string>("sub"));
        Assert.Equal(3, jwt.GetPayloadValue<int>("deviceId"));
    }

    // A device token holds its tablet and says it is a device token. It names no employee.
    [Fact]
    public async Task DeviceToken_HoldsTheDeviceIdAndNoEmployee() {
        var token = _tokens.CreateDeviceToken(deviceId: 5);

        var user = await SignedInAs(token);

        Assert.Equal(TokenClaims.DeviceKind, user.FindFirst(TokenClaims.Kind)?.Value);
        Assert.Equal(5, user.GetDeviceId());
        Assert.Null(user.GetEmployeeId());
    }

    // Only the fields in auth.md's table, so nothing extra (like nbf) ends up in the token.
    [Fact]
    public void Tokens_HoldOnlyTheFieldsInAuthMd() {
        var device = new JsonWebToken(_tokens.CreateDeviceToken(5));
        var session = new JsonWebToken(_tokens.CreateSessionToken(7, 5));

        Assert.Equal(new[] { "deviceId", "exp", "iat", "kind" }, device.Claims.Select(c => c.Type).Order().ToArray());
        Assert.Equal(new[] { "deviceId", "exp", "iat", "kind", "sub" }, session.Claims.Select(c => c.Type).Order().ToArray());
    }

    // How long each lasts

    [Fact]
    public void DeviceToken_Lasts90Days() {
        var jwt = new JsonWebToken(_tokens.CreateDeviceToken(5));

        Assert.Equal(TimeSpan.FromDays(90), jwt.ValidTo - jwt.IssuedAt);
    }

    [Fact]
    public void SessionToken_Lasts12Hours() {
        var jwt = new JsonWebToken(_tokens.CreateSessionToken(7, 5));

        Assert.Equal(TimeSpan.FromHours(12), jwt.ValidTo - jwt.IssuedAt);
    }

    // A session token made 13 hours ago has expired.
    [Fact]
    public async Task SessionToken_After12Hours_IsRefusedAsExpired() {
        var madeEarlier = new TokenService(Secret, new FixedClock(DateTimeOffset.UtcNow.AddHours(-13)));
        var token = madeEarlier.CreateSessionToken(7, 5);

        var result = await ValidateAsync(token);

        Assert.False(result.IsValid);
        Assert.IsAssignableFrom<SecurityTokenExpiredException>(result.Exception);
    }

    // An expired device token gets deviceExpired, so the app opens sign-back-in, not tablet setup.
    [Fact]
    public async Task DeviceToken_After90Days_GetsDeviceExpired() {
        var madeEarlier = new TokenService(Secret, new FixedClock(DateTimeOffset.UtcNow.AddDays(-91)));
        var result = await ValidateAsync(madeEarlier.CreateDeviceToken(5));

        Assert.False(result.IsValid);
        Assert.Equal(AuthErrors.DeviceExpired, TokenEvents.CodeForFailedToken(TokenClaims.DeviceKind, result.Exception));
    }

    // An expired session token is sessionEnded. Only device tokens get deviceExpired (auth.md).
    [Fact]
    public async Task SessionToken_Expired_GetsSessionEnded() {
        var madeEarlier = new TokenService(Secret, new FixedClock(DateTimeOffset.UtcNow.AddHours(-13)));
        var result = await ValidateAsync(madeEarlier.CreateSessionToken(7, 5));

        Assert.Equal(AuthErrors.SessionEnded, TokenEvents.CodeForFailedToken(TokenClaims.SessionKind, result.Exception));
    }

    // Signature

    // A token signed with another secret (for example from someone's laptop, sent to the live server) is refused.
    [Fact]
    public async Task Token_SignedWithAnotherSecret_IsRefused() {
        var otherServer = new TokenService("a-different-secret-that-is-also-long-enough");
        var token = otherServer.CreateSessionToken(7, 5);

        var result = await ValidateAsync(token);

        Assert.False(result.IsValid);
    }

    // Changing the payload breaks the signature, so a tablet can't make itself another employee.
    [Fact]
    public async Task Token_WithChangedPayload_IsRefused() {
        var parts = _tokens.CreateSessionToken(7, 5).Split('.');
        var otherPayload = _tokens.CreateSessionToken(8, 5).Split('.')[1];

        var result = await ValidateAsync($"{parts[0]}.{otherPayload}.{parts[2]}");

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("1234567890123456789012345678901")] // 31 characters
    public void Constructor_SecretUnder32Characters_Throws(string secret) {
        Assert.Throws<InvalidOperationException>(() => new TokenService(secret));
    }

    // Policies: one token can't be used in place of the other

    // A device token is refused where a session token is needed.
    [Fact]
    public async Task DeviceToken_IsRefusedBySessionPolicy() {
        var user = await SignedInAs(_tokens.CreateDeviceToken(5));

        var result = await Policies().AuthorizeAsync(user, AuthPolicies.Session);

        Assert.False(result.Succeeded);
    }

    // And the server's reply for it is sessionEnded, so the app opens employee sign-in.
    [Fact]
    public void DeviceToken_OnSessionEndpoint_GetsSessionEnded() {
        Assert.Equal(AuthErrors.SessionEnded, TokenEvents.CodeForWrongKind(TokenClaims.SessionKind, TokenClaims.DeviceKind));
    }

    [Fact]
    public async Task SessionToken_IsAcceptedBySessionPolicy() {
        var user = await SignedInAs(_tokens.CreateSessionToken(7, 5));

        var result = await Policies().AuthorizeAsync(user, AuthPolicies.Session);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SessionToken_IsRefusedByDevicePolicy() {
        var user = await SignedInAs(_tokens.CreateSessionToken(7, 5));

        var result = await Policies().AuthorizeAsync(user, AuthPolicies.Device);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthErrors.DeviceNotSetUp, TokenEvents.CodeForWrongKind(TokenClaims.DeviceKind, TokenClaims.SessionKind));
    }

    [Fact]
    public async Task DeviceToken_IsAcceptedByDevicePolicy() {
        var user = await SignedInAs(_tokens.CreateDeviceToken(5));

        var result = await Policies().AuthorizeAsync(user, AuthPolicies.Device);

        Assert.True(result.Succeeded);
    }

    // No token at all: a session endpoint says sessionEnded, a device endpoint says deviceNotSetUp.
    [Fact]
    public void NoToken_GetsTheCodeForTheEndpointsKind() {
        Assert.Equal(AuthErrors.SessionEnded, TokenEvents.CodeForFailedToken(TokenClaims.SessionKind, null));
        Assert.Equal(AuthErrors.DeviceNotSetUp, TokenEvents.CodeForFailedToken(TokenClaims.DeviceKind, null));
    }

    // Renew: POST /api/devices/renew takes a device token even after it expires

    [Fact]
    public async Task ReadDeviceIdIgnoringExpiry_ExpiredDeviceToken_GivesItsDeviceId() {
        var madeEarlier = new TokenService(Secret, new FixedClock(DateTimeOffset.UtcNow.AddDays(-200)));

        Assert.Equal(5, await _tokens.ReadDeviceIdIgnoringExpiryAsync(madeEarlier.CreateDeviceToken(5)));
    }

    [Fact]
    public async Task ReadDeviceIdIgnoringExpiry_CurrentDeviceToken_GivesItsDeviceId() {
        Assert.Equal(5, await _tokens.ReadDeviceIdIgnoringExpiryAsync(_tokens.CreateDeviceToken(5)));
    }

    // Only the signature is skipped for exp. A token from another secret is still refused.
    [Fact]
    public async Task ReadDeviceIdIgnoringExpiry_OtherSecret_GivesNull() {
        var otherServer = new TokenService("a-different-secret-that-is-also-long-enough");

        Assert.Null(await _tokens.ReadDeviceIdIgnoringExpiryAsync(otherServer.CreateDeviceToken(5)));
    }

    // A session token can't be used to renew a tablet.
    [Fact]
    public async Task ReadDeviceIdIgnoringExpiry_SessionToken_GivesNull() {
        Assert.Null(await _tokens.ReadDeviceIdIgnoringExpiryAsync(_tokens.CreateSessionToken(7, 5)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.b.c")]
    public async Task ReadDeviceIdIgnoringExpiry_MissingOrGarbage_GivesNull(string? token) {
        Assert.Null(await _tokens.ReadDeviceIdIgnoringExpiryAsync(token));
    }

    // A clock stuck at one time, for making tokens that are already expired.
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
