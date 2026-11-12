using Imp.Api.Data;
using Imp.Api.Errors;
using Serilog;
using Serilog.Events;
using Imp.Api.Security;

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

builder.Services.AddControllers()
	.ConfigureApiBehaviorOptions(options => {
        // Weird errors become a normal ApiError
        options.InvalidModelStateResponseFactory = _ =>
            throw new ApiError(StatusCodes.Status400BadRequest, "The request body is invalid.");
    }
);
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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

		if (apiError is not null) {
			await context.Response.WriteAsJsonAsync(new { error = apiError.Message, code = apiError.Code });
		} else {
			await context.Response.WriteAsJsonAsync(new {
				error = "Something went wrong. Please try again later.",
				traceId
			});
		}
	}
});

app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "IMP API"));
    app.UseCors("Dev");
}

app.UseAuthorization();

app.MapControllers();

app.Run();
