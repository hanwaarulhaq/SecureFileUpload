using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace WebApplication.Middlewares
{
    public class PathTraversalProtectionMiddleware
    {
        private readonly RequestDelegate _next;

        public PathTraversalProtectionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            // Check for path traversal patterns in the URL
            if (context.Request.Path.Value.Contains("../") ||
                context.Request.Path.Value.Contains("..\\") ||
                context.Request.Path.Value.Contains("%2e%2e") ||
                context.Request.Path.Value.Contains("%2e%2e/") ||
                context.Request.Path.Value.Contains("..%5c"))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Path traversal attempt detected");
                return;
            }

            await _next(context);
        }
    }
}
