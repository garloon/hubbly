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

        // Настройка сервисов
        ConfigureServices(builder.Services, configuration, environment);

        var app = builder.Build();

        await ApplyMigrations(app);

        // Настройка middleware
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
                throw; // Приложение не запустится
            }
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddControllers();

        // Настройка Health Checks
        ConfigureHealthChecks(services, configuration);

        // Swagger
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        // Database
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        // JWT
        ConfigureJwt(services, configuration);

        // CORS
        ConfigureCors(services);

        // SignalR
        ConfigureSignalR(services);

        // Rate Limiting
        ConfigureRateLimiting(services);

        // Cache
        services.AddMemoryCache();

        // Options
        services.Configure<RoomServiceOptions>(configuration.GetSection("Rooms"));

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // Services
        services.AddSingleton<JwtTokenService>();
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

    private static void ConfigureCors(IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("AllowMobileApp", policy =>
            {
                policy.WithOrigins(
                        "http://localhost:5000",
                        "http://127.0.0.1:5000",
                        "http://10.0.2.2:5000",
                        "http://192.168.0.103:5000",
                        "http://127.0.0.1:5500"
                    )
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials()
                    .SetIsOriginAllowedToAllowWildcardSubdomains();
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
            // Здесь можно добавить реальную проверку SignalR хаба
            // Например, проверить, что хаб зарегистрирован в маршрутах
            return Task.FromResult(HealthCheckResult.Healthy("SignalR Hub is configured"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy("SignalR Hub is not responding", ex));
        }
    }
}