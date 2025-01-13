using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Primitives;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();

// Add Error Handling Middleware
app.UseMiddleware<ErrorHandlingMiddleware>();
// Add Authentication Middleware
app.UseMiddleware<AuthenticationMiddleware>();
// Add Logging Middleware
app.UseMiddleware<LoggingMiddleware>();

ConcurrentDictionary<int, User> users = new ConcurrentDictionary<int, User>();
int nextId = 1;

app.MapGet("/", () => "Hello World!");

// CRUD Endpoints
app.MapGet("/users", () =>
{
    return TypedResults.Ok(users.Values.ToList());
});

app.MapGet("/users/{id}", Results<Ok<User>, NotFound> (int id) =>
{
    if (!users.TryGetValue(id, out var foundUser))
    {
        return TypedResults.NotFound();
    }
    return TypedResults.Ok(foundUser);
});

app.MapPost("/users", Results<Created<User>, BadRequest<string>> (User user) =>
{
    if (!User.IsValidUser(user))
    {
        return TypedResults.BadRequest("Invalid user data.");
    }

    int id = nextId;
    Interlocked.Increment(ref nextId);

    users[id] = user;
    return TypedResults.Created($"/users/{id}", user);
});

app.MapPut("/users/{id}", Results<Ok<User>, NotFound, BadRequest<string>> (int id, User user) =>
{
    if (!User.IsValidUser(user))
    {
        return TypedResults.BadRequest("Invalid user data.");
    }

    if (!users.TryGetValue(id, out var foundUser))
    {
        return TypedResults.NotFound();
    }

    foundUser.Name = user.Name;
    foundUser.Age = user.Age;
    foundUser.Email = user.Email;
    return TypedResults.Ok(foundUser);
});

app.MapDelete("/users/{id}", Results<NoContent, NotFound> (int id) =>
{
    if (!users.ContainsKey(id))
    {
        return TypedResults.NotFound();
    }

    users.TryRemove(id, out _);
    return TypedResults.NoContent();
});

// Endpoint for testing Error Handling Middleware
app.MapGet("/error", () =>
{
    throw new InvalidOperationException("This endpoint throws an exception");
});

app.Run();

public class User
{
    required public string Name { get; set; }
    required public string Email { get; set; }
    public int Age { get; set; }

    public static bool IsValidUser(User user)
    {
        return !string.IsNullOrWhiteSpace(user.Name) &&
               user.Age > 0 &&
               !string.IsNullOrWhiteSpace(user.Email) &&
               Regex.IsMatch(user.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    }
}

// Error Handling Middleware
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception has occurred.");
            await HandleExceptionAsync(context);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var response = new { error = "Internal server error." };
        var jsonResponse = JsonSerializer.Serialize(response);

        return context.Response.WriteAsync(jsonResponse);
    }
}

// Authentication Middleware
public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthenticationMiddleware> _logger;

    public AuthenticationMiddleware(RequestDelegate next, ILogger<AuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var tokenValues) || StringValues.IsNullOrEmpty(tokenValues))
        {
            _logger.LogWarning("Authorization header missing.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        var token = tokenValues.ToString();

        if (!ValidateToken(token))
        {
            _logger.LogWarning("Invalid token.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        await _next(context);
    }

    private bool ValidateToken(string token)
    {
        return token == "valid-token";
    }
}

// Logging Middleware
public class LoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(RequestDelegate next, ILogger<LoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        _logger.LogInformation("HTTP {Method} {Path}", context.Request.Method, context.Request.Path);
        await _next(context);
        _logger.LogInformation("Response Status Code: {StatusCode}", context.Response.StatusCode);
    }
}