using Aion.Core.Auth;

namespace Aion.Core.Middleware;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly string[] PublicPaths = {
        "/api/health",
        "/api/config",
        "/api/login",
        "/api/auto-login",
        "/api/setup",
        "/hub/",
        "/login",
        "/favicon.svg",
        "/favicon.png",
        "/assets/"
    };

    public AuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AuthService auth)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Serve /login as /login.html
        if (path == "/login")
        {
            context.Response.Redirect("/login.html");
            return;
        }

        // Public paths pass through
        foreach (var p in PublicPaths)
        {
            if (path.StartsWith(p))
            {
                await _next(context);
                return;
            }
        }

        // GET requests for SPA files (React handles routing) — allow through
        if (context.Request.Method == "GET" && !path.StartsWith("/api/"))
        {
            await _next(context);
            return;
        }

        // API calls need authentication
        if (path.StartsWith("/api/"))
        {
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            var token = authHeader?.Replace("Bearer ", "");

            if (string.IsNullOrEmpty(token))
            {
                token = context.Request.Query["token"].FirstOrDefault();
                token ??= context.Request.Cookies["aion_token"];
            }

            var userId = await auth.ValidateTokenAsync(token);
            if (userId == null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { ok = false, error = "Authentication required. Login at /login first.", error_code = "AUTH_REQUIRED" });
                return;
            }

            context.Items["UserId"] = userId;
        }

        await _next(context);
    }
}

public static class AuthMiddlewareExtensions
{
    public static IApplicationBuilder UseAionAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuthMiddleware>();
    }
}
