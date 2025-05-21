using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading.Tasks;

namespace WebApplication.Middlewares
{
    public class FileNameSecurityMiddleware
    {
        private readonly RequestDelegate _next;

        public FileNameSecurityMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            if (context.Request.HasFormContentType)
            {
                var form = await context.Request.ReadFormAsync();

                foreach (var file in form.Files)
                {
                    // Check for alternate data streams
                    if (file.FileName.Contains(":"))
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsync("Invalid file name: alternate data stream detected");
                        return;
                    }

                    // Check for double extensions
                    var ext = Path.GetExtension(file.FileName);
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(file.FileName);
                    if (nameWithoutExt != null && Path.HasExtension(nameWithoutExt))
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsync("Invalid file name: double extension detected");
                        return;
                    }
                }
            }

            await _next(context);
        }
    }
}
