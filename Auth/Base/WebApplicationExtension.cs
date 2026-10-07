using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;

namespace Zuhid.Auth.Base;

public static class WebApplicationExtension
{
    public static void AddServices<TSetting>(this WebApplicationBuilder builder, TSetting setting) where TSetting : SettingBase
    {
        builder.Services.AddSingleton(setting);
        builder.AddJwtAuthentication(setting);
        builder.Services.AddAuthorization();
        builder.Services.AddControllers(options =>
        {
            options.Filters.Add<ActionFilter>();
            options.Filters.Add<ExceptionFilter>();
            options.Conventions.Add(new ControllerFolderRouteConvention());
        });
        builder.AddCorsPolicy();
        builder.AddSwagger(setting);
    }

    private static void AddJwtAuthentication<TSetting>(this WebApplicationBuilder builder, TSetting setting) where TSetting : SettingBase
    {
        builder.Services.AddAuthentication(option =>
         {
             option.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
             option.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
         })
            .AddJwtBearer(options => ConfigureJwtBearer(options, setting));
    }

    private static void ConfigureJwtBearer<TSetting>(JwtBearerOptions options, TSetting setting) where TSetting : SettingBase
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(setting.Jwt.PublicKeyPath));
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = setting.Jwt.Issuer,
            ValidAudience = setting.Jwt.Audience,
            IssuerSigningKey = new RsaSecurityKey(rsa),
            RoleClaimType = ClaimTypes.Role,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = OnTokenValidated,
            OnAuthenticationFailed = OnAuthenticationFailed,
            OnChallenge = OnChallenge,
            OnForbidden = OnForbidden
        };
    }

    private static Task OnTokenValidated(TokenValidatedContext context)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
        logger.LogDebug("Authentication validated for token. Request Path: {Path}", context.HttpContext.Request.Path);
        return Task.CompletedTask;
    }

    private static Task OnAuthenticationFailed(AuthenticationFailedContext context)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
        logger.LogError(context.Exception, "Authentication failed for token. Request Path: {Path}", context.HttpContext.Request.Path);
        return Task.CompletedTask;
    }

    private static Task OnChallenge(JwtBearerChallengeContext context)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
        logger.LogWarning("JWT Challenge: {Error}, {ErrorDescription}. AuthenticateFailure: {Failure}", context.Error, context.ErrorDescription, context.AuthenticateFailure?.Message);
        return Task.CompletedTask;
    }

    private static Task OnForbidden(ForbiddenContext context)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
        logger.LogWarning("JWT Forbidden for user: {User}", context.HttpContext.User.Identity?.Name);
        return Task.CompletedTask;
    }

    private static void AddCorsPolicy(this WebApplicationBuilder builder)
    {
        builder.Services.AddCors(options => options.AddPolicy(name: "CorsPolicy", policy => policy
          .AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader()
        ));
    }

    private static void AddSwagger<TSetting>(this WebApplicationBuilder builder, TSetting setting) where TSetting : SettingBase
    {
        builder.Services.AddSwaggerGen(c =>
        {
            c.EnableAnnotations(); // honor [SwaggerSchema] and friends on models
            c.SwaggerDoc("v1", new OpenApiInfo { Title = setting.Name, Version = setting.Version });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                Name = "Authorization",
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
            });
            c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });
    }

    public static WebApplication BuildServices(this WebApplicationBuilder builder, SettingBase setting)
    {
        var app = builder.Build();
        app.UseCors("CorsPolicy");
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint($"/swagger/{setting.Version}/swagger.json", $"{setting.Name} {setting.Version}");
            c.RoutePrefix = "swagger"; // Set Swagger UI at /swagger
        });
        app.MapGet("/", async context => await context.Response.WriteAsync("""
    <html>
    <body style='padding:100px 0;text-align:center;font-size:xxx-large;'>
        <a href='./swagger/index.html'>View Swagger</a>
    </body>
    </html>
    """));
        app.MapControllers().RequireAuthorization();
        return app;
    }

    public static void AddScopedByConvention(this WebApplicationBuilder builder, Assembly codeEssembly, string[] scopedSuffixes)
    {
        builder.Services.AddScoped<HttpClient>();
        codeEssembly.GetTypes()
            .Where(s => s.IsClass && !s.IsAbstract && scopedSuffixes.Any(suffix => s.Name.EndsWith(suffix)))
            .ToList()
            .ForEach(item => builder.Services.AddScoped(item));
    }

    public static void AddPostgres<TContext>(this WebApplicationBuilder builder, string connectionString) where TContext : DbContext
    {
        builder.Services.AddDbContext<TContext>(options => options
          .UseNpgsql(connectionString)
          .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking) // setting to no tracking to improve performance
        );
    }
}
