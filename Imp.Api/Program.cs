using Imp.Api.Data;
using Imp.Api.Errors;
using Serilog;
using Serilog.Events;
using Imp.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi;

Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

// Set up the error logger
builder.Host.UseSerilog((context, services, config) => config
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "imp-.log"),
        restrictedToMinimumLevel: LogEventLevel.Warning,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30
    )
);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("IMP")
    ?? throw new InvalidOperationException("Connection string 'IMP' was not found. Check Imp.Api/appsettings.Development.json.");
builder.Services.AddSingleton<IDbConnectionFactory>(new MySqlConnectionFactory(connectionString));
builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();

// Sign-in tokens (docs/api/auth.md). The secret comes from appsettings.Development.json on your computer,
// and from the Jwt__Secret environment variable on the live server.
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret was not found. Check Imp.Api/appsettings.Development.json, or set Jwt__Secret on the server.");
var tokenService = new TokenService(jwtSecret);
builder.Services.AddSingleton(tokenService);
builder.Services.AddSingleton<TokenEvents>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => {
        // Keep the claim names as they are in the token (sub, kind, deviceId) instead of long .NET names.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = tokenService.ValidationParameters;
        // TokenEvents does the database checks (TokenValidated) and writes the 401 reply (Challenge).
        options.EventsType = typeof(TokenEvents);
    });

// Endpoints use [Authorize(Policy = "Session")] or [Authorize(Policy = "Device")]. Each checks the token's kind.
builder.Services.AddAuthorization(AuthPolicies.AddTo);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options => {
        // Weird errors become a normal ApiError
        options.InvalidModelStateResponseFactory = _ =>
            throw new ApiError(StatusCodes.Status400BadRequest, "The request body is invalid.");
    }
);
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options => {
    // Adds the Authorize button to Swagger, where a device or session token can be pasted in.
    options.AddDocumentTransformer((document, context, ct) => {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste a device or session token, without the word Bearer.",
        };
        document.Security = [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }
        ];
        return Task.CompletedTask;
    });
});

builder.Services.AddCors(o => o.AddPolicy("Dev", p =>  
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Error handler: must stay first so it catches errors from everything below it.
app.Use(async (context, next) => {
    try {
        await next();
    } catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
        // Caller disconnected, no one to give error to
    } catch (Exception ex) {
        var traceId = context.TraceIdentifier;
        var apiError = ex as ApiError;
        var status = apiError?.StatusCode ?? StatusCodes.Status500InternalServerError;

        // Log real exception with stack trace
        app.Logger.Log(
            status >= 500 ? LogLevel.Error : LogLevel.Warning, ex,
            "{Method} {Path} failed with {Status} (trace {TraceId})",
            context.Request.Method, context.Request.Path, status, traceId);

        // Too late to change the response
        if (context.Response.HasStarted) throw;

        context.Response.StatusCode = status;

        // code is left out entirely when there isn't one (docs/api/README.md)
        if (apiError is null) {
            await context.Response.WriteAsJsonAsync(new {
                error = "Something went wrong. Please try again later.",
                traceId
            });
        } else if (apiError.Code is null) {
            await context.Response.WriteAsJsonAsync(new { error = apiError.Message });
        } else {
            await context.Response.WriteAsJsonAsync(new { error = apiError.Message, code = apiError.Code });
        }
    }
});

// Errors the framework sends with no body (an unknown URL, the wrong method) get the same JSON shape.
app.UseStatusCodePages(async statusContext => {
    var response = statusContext.HttpContext.Response;
    var error = response.StatusCode switch {
        StatusCodes.Status404NotFound => "This address doesn't exist on the server.",
        StatusCodes.Status405MethodNotAllowed => "This address doesn't accept this kind of request.",
        _ => "The request couldn't be completed."
    };
    await response.WriteAsJsonAsync(new { error });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "IMP API"));
    app.UseCors("Dev");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
