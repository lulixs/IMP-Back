namespace Imp.Api.Errors;

public class ApiError : Exception {
    public int StatusCode { get; }

    public ApiError(int statusCode, string message, Exception? innerException = null) : base(message, innerException) {
        StatusCode = statusCode;
    }

    // Shortcuts for the common cases: throw ApiError.NotFound("Box not found");
    public static ApiError BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
    public static ApiError Unauthorized(string message) => new(StatusCodes.Status401Unauthorized, message);
    public static ApiError Forbidden(string message) => new(StatusCodes.Status403Forbidden, message);
    public static ApiError NotFound(string message) => new(StatusCodes.Status404NotFound, message);
    public static ApiError Conflict(string message) => new(StatusCodes.Status409Conflict, message);
}