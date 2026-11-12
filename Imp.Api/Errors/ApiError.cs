namespace Imp.Api.Errors;

public class ApiError : Exception {
    public int StatusCode { get; }
    public string? Code { get; }

    public ApiError(int statusCode, string? message, string? code = null, Exception? innerException = null) : base(message, innerException) {
        StatusCode = statusCode;
        Code = code;
    }

    // Shortcuts for the common cases: throw ApiError.NotFound("Box not found.");
    public static ApiError BadRequest(string? message, string? code = null) => new(StatusCodes.Status400BadRequest, message, code);
    public static ApiError Unauthorized(string? message, string? code = null) => new(StatusCodes.Status401Unauthorized, message, code);
    public static ApiError Forbidden(string? message, string? code = null) => new(StatusCodes.Status403Forbidden, message, code);
    public static ApiError NotFound(string? message, string? code = null) => new(StatusCodes.Status404NotFound, message, code);
    public static ApiError Conflict(string? message, string? code = null) => new(StatusCodes.Status409Conflict, message, code);
}