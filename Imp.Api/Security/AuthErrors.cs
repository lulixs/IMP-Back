namespace Imp.Api.Security;

// The four token errors from the Errors table in docs/api/auth.md. Each is a 401 with a code,
// because the app has to leave the current screen (D-168). Copy any change from auth.md exactly.
public static class AuthErrors {
    public const string DeviceNotSetUp = "deviceNotSetUp";
    public const string DeviceExpired = "deviceExpired";
    public const string DeviceRemoved = "deviceRemoved";
    public const string SessionEnded = "sessionEnded";

    public static string MessageFor(string code) => code switch {
        DeviceNotSetUp => "This tablet isn't set up. Set it up again.",
        DeviceExpired => "Sign this tablet back in.",
        DeviceRemoved => "This tablet was removed. Set it up again.",
        _ => "You've been signed out. Sign in again.",
    };

    // The code for a token that is missing, not valid, or the wrong kind, on an endpoint that wants
    // requiredKind. A device token on a session endpoint is sessionEnded, and the other way round is deviceNotSetUp.
    public static string ForMissingToken(string? requiredKind) =>
        requiredKind == TokenClaims.DeviceKind ? DeviceNotSetUp : SessionEnded;
}
