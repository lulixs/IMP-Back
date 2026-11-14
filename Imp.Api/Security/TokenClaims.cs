using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Imp.Api.Security;

// The names inside the tokens, as listed in docs/api/auth.md.
public static class TokenClaims {
    public const string Kind = "kind";
    public const string EmployeeId = "sub";
    public const string DeviceId = "deviceId";

    // The two values of kind. kind stops one token from being used in place of the other.
    public const string DeviceKind = "device";
    public const string SessionKind = "session";

    // The employee signed in with this session token, or null for a device token.
    // Use it in a controller as User.GetEmployeeId().
    public static int? GetEmployeeId(this ClaimsPrincipal user) => ReadInt(user, EmployeeId);

    // The tablet this token belongs to. Both kinds of token hold it.
    public static int? GetDeviceId(this ClaimsPrincipal user) => ReadInt(user, DeviceId);

    private static int? ReadInt(ClaimsPrincipal user, string claimType) {
        var value = user.FindFirst(claimType)?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}

// The two policies endpoints use: [Authorize(Policy = AuthPolicies.Session)] or [Authorize(Policy = AuthPolicies.Device)].
// Which endpoints take which token is in the table at the top of docs/api/auth.md.
public static class AuthPolicies {
    public const string Device = "Device";
    public const string Session = "Session";

    // Registers both policies. Used by Program.cs and by TokenServiceTests, so they test the same rules.
    public static void AddTo(AuthorizationOptions options) {
        options.AddPolicy(Device, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(TokenClaims.Kind, TokenClaims.DeviceKind));
        options.AddPolicy(Session, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(TokenClaims.Kind, TokenClaims.SessionKind));
    }

    // The token kind the endpoint asks for, from its [Authorize(Policy = ...)], or null if it asks for none.
    public static string? RequiredKind(Endpoint? endpoint) {
        if (endpoint is null) return null;
        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
        if (policies.Contains(Session)) return TokenClaims.SessionKind;
        if (policies.Contains(Device)) return TokenClaims.DeviceKind;
        return null;
    }
}
