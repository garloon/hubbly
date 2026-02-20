using Hubbly.Api.Hubs;
using Hubbly.Api.Middleware;
using Hubbly.Application.Services;
using Hubbly.Domain.Common;
using Hubbly.Domain.Services;
using Hubbly.Infrastructure.Data;
using Hubbly.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace Hubbly.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var configuration = builder.Configuration;
        var environment = builder.Environment;

        // Configure services
        ConfigureServices(builder.Services, configuration, environment);

        var app = builder.Build();

        await ApplyMigrations(app);

        // Configure middleware
        ConfigureMiddleware(app, environment);

        await app.RunAsync();
    }

    private static async Task ApplyMigrations(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        try
        {
            var dbContext = services.GetRequiredService<AppDbContext>();
            
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
            var pendingList = pendingMigrations.ToList();

            if (pendingList.Any())
                await dbContext.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "❌ An error occurred while migrating the database");
            
            if (app.Environment.IsProduction())
            {
                throw; // Application will not start
            }
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddControllers();

        // Configure health checks
        ConfigureHealthChecks(services, configuration);

        // Swagger
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        // Database with Polly retry policy
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null);
                }));

        // JWT
        ConfigureJwt(services, configuration);

        // CORS
        ConfigureCors(services, configuration);

        // SignalR
        ConfigureSignalR(services);

        // Rate Limiting
        ConfigureRateLimiting(services);

        // Cache with size limit to prevent memory leaks
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 1024 * 1024; // 1MB limit
            options.ExpirationScanFrequency = TimeSpan.FromMinutes(1);
        });

        // Options
        services.Configure<RoomServiceOptions>(configuration.GetSection("Rooms"));

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // Services
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IAvatarValidator, AvatarValidator>();

        // Singletons
        services.AddSingleton<IRoomService, RoomService>();

        // Hosted Services
        services.AddHostedService<RoomCleanupService>();
    }

    private static void ConfigureHealthChecks(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddNpgSql(
                configuration.GetConnectionString("DefaultConnection")!,
                name: "database",
                tags: new[] { "ready", "live" })
            .AddUrlGroup(
                new Uri("http://localhost:5000/avatars/male_base.glb"),
                name: "3d-models",
                tags: new[] { "ready" })
            .AddCheck<SignalRHealthCheck>(
                name: "signalr",
                tags: new[] { "ready" });
    }

    private static void ConfigureJwt(IServiceCollection services, IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>();
        services.AddSingleton(jwtSettings!);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings!.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;

                    if (!string.IsNullOrEmpty(accessToken) &&
                        (path.StartsWithSegments("/chatHub") || path.Value?.Contains("chatHub") == true))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });
    }

    private static void ConfigureCors(IServiceCollection services, IConfiguration configuration)
    {
        var corsOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        
        services.AddCors(options =>
        {
            options.AddPolicy("AllowMobileApp", policy =>
            {
                if (corsOrigins != null && corsOrigins.Length > 0)
                {
                    policy.WithOrigins(corsOrigins)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials()
                        .SetIsOriginAllowedToAllowWildcardSubdomains();
                }
                else
                {
                    // Fallback if configuration is missing
                    policy.WithOrigins(
                            "http://localhost:5000",
                            "http://127.0.0.1:5000"
                        )
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials()
                        .SetIsOriginAllowedToAllowWildcardSubdomains();
                }
            });
        });
    }

    private static void ConfigureSignalR(IServiceCollection services)
    {
        services.AddSignalR(hubOptions =>
        {
            hubOptions.MaximumParallelInvocationsPerClient = 10;
            hubOptions.MaximumReceiveMessageSize = 65536;
            hubOptions.EnableDetailedErrors = false;
            hubOptions.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            hubOptions.KeepAliveInterval = TimeSpan.FromSeconds(15);
        });
    }

    private static void ConfigureRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.Identity?.Name ??
                                   httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 100,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            options.AddFixedWindowLimiter("Auth", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = 5;
                opt.QueueLimit = 0;
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "Too many requests. Please try again later."
                }), cancellationToken);
            };
        });
    }

    private static void ConfigureMiddleware(WebApplication app, IHostEnvironment environment)
    {
        // Swagger
        if (environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // Static files
        ConfigureStaticFiles(app);

        // Middleware
        app.UseRateLimiter();
        app.UseCors("AllowMobileApp");
        app.UseAuthentication();
        app.UseAuthorization();

        // Global exception handler
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
                var exception = exceptionHandlerPathFeature?.Error;
                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

                if (exception != null)
                {
                    logger.LogError(exception, "Unhandled exception occurred on path: {Path}", context.Request.Path);
                }

                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";

                var response = new
                {
                    error = "An internal server error occurred. Please try again later.",
                    // Only show details in development
                    details = context.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() == true && exception != null
                        ? exception.Message
                        : null
                };

                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
            });
        });

        // Request logging middleware
        app.UseMiddleware<RequestLoggingMiddleware>();

        // Map endpoints
        app.MapControllers();
        app.MapHub<ChatHub>("/chatHub", options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.ApplicationMaxBufferSize = 65536;
            options.TransportMaxBufferSize = 65536;
        });

        // Health checks
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthResponse
        });

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            ResponseWriter = WriteHealthResponse
        });

        app.Run("http://0.0.0.0:5000");
    }

    private static void ConfigureStaticFiles(WebApplication app)
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".glb"] = "model/gltf-binary";
        provider.Mappings[".gltf"] = "model/gltf+json";

        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = provider,
            ServeUnknownFileTypes = true,
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
            }
        });
    }

    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration,
                description = e.Value.Description
            }),
            timestamp = DateTime.UtcNow
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}

public class SignalRHealthCheck : IHealthCheck
{
    private readonly ILogger<SignalRHealthCheck> _logger;

    public SignalRHealthCheck(ILogger<SignalRHealthCheck> logger)
    {
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // You can add real SignalR hub check here
            // For example, check that the hub is registered in routes
            return Task.FromResult(HealthCheckResult.Healthy("SignalR Hub is configured"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy("SignalR Hub is not responding", ex));
        }
    }
}