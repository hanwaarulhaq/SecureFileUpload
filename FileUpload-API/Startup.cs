using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebApplication.Services;
using Swashbuckle.AspNetCore.Swagger;
using WebApplication.Middlewares;


namespace WebApplication
{
    // Startup.cs
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            // Configure CORS first
            services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyMethod()
                           .AllowAnyHeader();

                    // If you need to support credentials:
                    // builder.WithOrigins("http://example.com")
                    //        .AllowCredentials();
                });
            });

            // Add MVC with compatibility version
            services.AddMvc()
                .SetCompatibilityVersion(Microsoft.AspNetCore.Mvc.CompatibilityVersion.Version_2_2);

            // Configure file upload limits
            services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = long.MaxValue;  // Unlimited upload size
                options.MemoryBufferThreshold = int.MaxValue;       // Important for large files
            });

            // Register application services
            services.AddScoped<FileUploadService>();
            services.AddSingleton<IAntivirusChecker, WinDefenderService>();

            // Add Swagger
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Info { Title = "Your API Name", Version = "v1" });
            });
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseMiddleware<PathTraversalProtectionMiddleware>();
            app.UseMiddleware<FileNameSecurityMiddleware>();
            // Enable CORS - must come before UseMvc
            app.UseCors("AllowAll");

            // Enable Swagger UI
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Your API V1");
            });


            // For .NET Core 2.2, we use UseMvc with default route
            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}