using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Imp.Api.Security;

// Makes the two tokens from docs/api/auth.md (D-163). Both are JWTs signed with Jwt:Secret,
// and nothing about them is stored in the database.
//   Device token:  kind "device", deviceId, iat, exp. Lasts 90 days.
//   Session token: kind "session", sub (the employee id), deviceId, iat, exp. Lasts 12 hours.
// The 90 days and 12 hours are placeholders until Thrift on Purpose confirms them (D-147).
public sealed class TokenService {
    public static readonly TimeSpan DeviceTokenLifetime = TimeSpan.FromDays(90);
    public static readonly TimeSpan SessionTokenLifetime = TimeSpan.FromHours(12);

    // HS256 needs a key of at least 256 bits, so 32 characters is the shortest secret allowed.
    public const int MinimumSecretLength = 32;

    private readonly TimeProvider clock;
    private readonly SigningCredentials signingCredentials;
    private readonly JsonWebTokenHandler handler = new() { SetDefaultTimesOnTokenCreation = false };

    // The settings JwtBearer (and the tests) use to check a token: our signature and an exp in the future.
    // There is no issuer or audience, since this server is the only one that makes or reads these tokens.
    public TokenValidationParameters ValidationParameters { get; }

    // The same checks without exp, for POST /api/devices/renew only.
    private readonly TokenValidationParameters ignoringExpiry;

    // clock is only passed by tests, to make tokens that are already expired.
    public TokenService(string secret, TimeProvider? clock = null) {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < MinimumSecretLength) {
            throw new InvalidOperationException(
                $"Jwt:Secret must be at least {MinimumSecretLength} characters. " +
                "Check Imp.Api/appsettings.Development.json, or the Jwt__Secret environment variable on the server.");
        }

        this.clock = clock ?? TimeProvider.System;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        ValidationParameters = new TokenValidationParameters {
            IssuerSigningKey = key,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            // The server makes and checks every token with its own clock, so no extra minutes are allowed.
            ClockSkew = TimeSpan.Zero,
        };

        ignoringExpiry = ValidationParameters.Clone();
        ignoringExpiry.ValidateLifetime = false;
    }

    // Sent to a tablet by POST /api/setup, POST /api/devices, and POST /api/devices/renew.
    public string CreateDeviceToken(int deviceId) {
        return Create(DeviceTokenLifetime, new Dictionary<string, object> {
            [TokenClaims.Kind] = TokenClaims.DeviceKind,
            [TokenClaims.DeviceId] = deviceId,
        });
    }

    // Sent by POST /api/setup and POST /api/sessions when an employee signs in on a tablet.
    public string CreateSessionToken(int employeeId, int deviceId) {
        return Create(SessionTokenLifetime, new Dictionary<string, object> {
            [TokenClaims.Kind] = TokenClaims.SessionKind,
            // JWTs keep sub as a string, for example "sub": "1" (auth.md).
            [TokenClaims.EmployeeId] = employeeId.ToString(CultureInfo.InvariantCulture),
            [TokenClaims.DeviceId] = deviceId,
        });
    }

    // For POST /api/devices/renew, which takes a device token whether or not it has expired (auth.md).
    // Checks the signature and that it is a device token, but not exp. Returns the tablet's id,
    // or null if the token is missing, not ours, changed, or a session token.
    public async Task<int?> ReadDeviceIdIgnoringExpiryAsync(string? token) {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var result = await handler.ValidateTokenAsync(token, ignoringExpiry);
        if (!result.IsValid) return null;

        var user = new ClaimsPrincipal(result.ClaimsIdentity);
        if (user.FindFirst(TokenClaims.Kind)?.Value != TokenClaims.DeviceKind) return null;
        return user.GetDeviceId();
    }

    private string Create(TimeSpan lifetime, Dictionary<string, object> claims) {
        var now = clock.GetUtcNow().UtcDateTime;
        return handler.CreateToken(new SecurityTokenDescriptor {
            Claims = claims,
            IssuedAt = now,
            Expires = now + lifetime,
            SigningCredentials = signingCredentials,
        });
    }
}
