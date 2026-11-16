using Dapper;
using Imp.Api.Data;
using Imp.Api.Errors;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Imp.Api.Security;

// Put on an endpoint (or a whole controller) that needs Master mode
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireMasterModeAttribute : Attribute, IAsyncActionFilter {
    public const string Message = "Turn on Master mode first.";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next) {
        var http = context.HttpContext;
        var deviceId = http.User.GetDeviceId();
        if (deviceId is null) {
            // No session token got this far, which means [Authorize] is missing.
            throw ApiError.Unauthorized(AuthErrors.MessageFor(AuthErrors.SessionEnded), AuthErrors.SessionEnded);
        }

        var db = http.RequestServices.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await db.OpenConnectionAsync(http.RequestAborted);
        var masterModeOn = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT is_elevated FROM DEVICE WHERE id = @deviceId",
            new { deviceId }, cancellationToken: http.RequestAborted));

        if (!masterModeOn) throw ApiError.Forbidden(Message);

        await next();
    }
}